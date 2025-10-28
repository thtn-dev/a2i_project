using A2I.Application.Notifications;
using A2I.Application.StripeAbstraction.Webhooks;
using A2I.Core.Enums;
using A2I.Infrastructure.Database;
using BuildingBlocks.Utils.Helpers;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Stripe;

namespace A2I.Infrastructure.StripeServices.WebhookHandlers;

public class InvoicePaidHandler : WebhookEventHandlerBase
{
    private readonly IEmailService _emailService;

    public InvoicePaidHandler(
        ApplicationDbContext db,
        IEmailService emailService,
        ILogger<InvoicePaidHandler> logger)
        : base(db, logger)
    {
        _emailService = emailService;
    }

    public override string EventType => EventTypes.InvoicePaid;

    protected override async Task<WebhookHandlerResult> HandleCoreAsync(
        Event stripeEvent,
        CancellationToken ct)
    {
        if (stripeEvent.Data.Object is not Invoice invoice)
            return new WebhookHandlerResult(false, "Invalid invoice data");

        if (invoice.Status != "paid")
            return new WebhookHandlerResult(
                true,
                $"Invoice status is {invoice.Status}, not paid");

        var customer = await Db.Customers
            .FirstOrDefaultAsync(c => c.StripeCustomerId == invoice.CustomerId, ct);

        if (customer == null)
        {
            Logger.LogError(
                "Customer not found for Stripe customer ID: {StripeCustomerId}",
                invoice.CustomerId);

            return new WebhookHandlerResult(
                false,
                "Customer not found",
                true);
        }

        var dbInvoice = await Db.Invoices
            .FirstOrDefaultAsync(i => i.StripeInvoiceId == invoice.Id, ct);

        if (dbInvoice == null)
        {
            dbInvoice = new Core.Entities.Invoice
            {
                Id = IdGenHelper.NewGuidId(),
                CustomerId = customer.Id,
                StripeInvoiceId = invoice.Id,
                StripePaymentIntentId = "",
                InvoiceNumber = invoice.Number,
                Status = InvoiceStatus.Paid,
                Amount = invoice.AmountDue / 100m, // Convert from cents
                AmountPaid = invoice.AmountPaid / 100m,
                AmountDue = 0, // Paid, so due = 0
                Currency = invoice.Currency ?? "usd",
                PeriodStart = invoice.PeriodStart,
                PeriodEnd = invoice.PeriodEnd,
                DueDate = invoice.DueDate,
                PaidAt = DateTime.UtcNow,
                AttemptCount = invoice.AttemptCount,
                HostedInvoiceUrl = invoice.HostedInvoiceUrl,
                InvoicePdf = invoice.InvoicePdf
            };

            // Link to subscription if exists
            if (!string.IsNullOrWhiteSpace(invoice.Parent.SubscriptionDetails.SubscriptionId))
            {
                var subscription = await Db.Subscriptions
                    .FirstOrDefaultAsync(
                        s => s.StripeSubscriptionId == invoice.Parent.SubscriptionDetails.SubscriptionId,
                        ct);

                if (subscription != null)
                {
                    dbInvoice.SubscriptionId = subscription.Id;
                }
            }

            Db.Invoices.Add(dbInvoice);

            Logger.LogInformation(
                "Created invoice {InvoiceId} for customer {CustomerId}",
                dbInvoice.Id, customer.Id);
        }
        else
        {
            dbInvoice.Status = InvoiceStatus.Paid;
            dbInvoice.AmountPaid = invoice.AmountPaid / 100m;
            dbInvoice.AmountDue = 0;
            dbInvoice.PaidAt = DateTime.UtcNow;
            dbInvoice.AttemptCount = invoice.AttemptCount;

            Logger.LogInformation(
                "Updated invoice {InvoiceId} to paid status",
                dbInvoice.Id);
        }

        if (dbInvoice.SubscriptionId.HasValue)
        {
            var subscription = await Db.Subscriptions
                .FindAsync([dbInvoice.SubscriptionId.Value], ct);

            if (subscription != null)
            {
                // Update subscription period
                subscription.CurrentPeriodStart = invoice.PeriodStart;
                subscription.CurrentPeriodEnd = invoice.PeriodEnd;

                // Reset to active if was past_due
                if (subscription.Status == SubscriptionStatus.PastDue)
                {
                    subscription.Status = SubscriptionStatus.Active;

                    Logger.LogInformation(
                        "Subscription {SubId} restored to active after payment",
                        subscription.Id);
                }

                if (subscription is { CancelAtPeriodEnd: true, Status: SubscriptionStatus.Active })
                {
                    // Don't auto-clear this - user might have manually set it
                    // Just log for awareness
                    Logger.LogInformation(
                        "Subscription {SubId} still set to cancel at period end despite payment",
                        subscription.Id);
                }
            }
        }

        await Db.SaveChangesAsync(ct);

        // Queue receipt email
        BackgroundJob.Enqueue(() =>
            _emailService.SendReceiptEmailAsync(
                customer.Id,
                dbInvoice.Id,
                CancellationToken.None));

        return new WebhookHandlerResult(
            true,
            $"Invoice {dbInvoice.Id} marked as paid",
            Metadata: new Dictionary<string, object>
            {
                ["invoice_id"] = dbInvoice.Id,
                ["customer_id"] = customer.Id,
                ["amount"] = dbInvoice.AmountPaid
            });
    }
}