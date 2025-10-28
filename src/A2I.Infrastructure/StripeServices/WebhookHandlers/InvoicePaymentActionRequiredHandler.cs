// src/A2I.Infrastructure/StripeServices/WebhookHandlers/InvoicePaymentActionRequiredHandler.cs

using A2I.Application.Notifications;
using A2I.Application.StripeAbstraction.Webhooks;
using A2I.Core.Enums;
using A2I.Infrastructure.Database;
using BuildingBlocks.Utils.Helpers;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Stripe;
using Invoice = A2I.Core.Entities.Invoice;
using StripeInvoice = Stripe.Invoice;

namespace A2I.Infrastructure.StripeServices.WebhookHandlers;

public class InvoicePaymentActionRequiredHandler : WebhookEventHandlerBase
{
    private readonly IEmailService _emailService;

    public InvoicePaymentActionRequiredHandler(
        ApplicationDbContext db,
        IEmailService emailService,
        ILogger<InvoicePaymentActionRequiredHandler> logger)
        : base(db, logger)
    {
        _emailService = emailService;
    }

    public override string EventType => EventTypes.InvoicePaymentActionRequired;

    protected override async Task<WebhookHandlerResult> HandleCoreAsync(
        Event stripeEvent,
        CancellationToken ct)
    {
        var invoice = stripeEvent.Data.Object as StripeInvoice;
        if (invoice == null) return new WebhookHandlerResult(false, "Invalid invoice data");

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
            // Create invoice with action required status
            dbInvoice = new Invoice
            {
                Id = IdGenHelper.NewGuidId(),
                CustomerId = customer.Id,
                StripeInvoiceId = invoice.Id,
                StripePaymentIntentId = "",
                InvoiceNumber = invoice.Number,
                Status = InvoiceStatus.Open, // Keep as open, waiting for action
                Amount = invoice.AmountDue / 100m,
                AmountPaid = invoice.AmountPaid / 100m,
                AmountDue = invoice.AmountRemaining / 100m,
                Currency = invoice.Currency ?? "usd",
                PeriodStart = invoice.PeriodStart,
                PeriodEnd = invoice.PeriodEnd,
                DueDate = invoice.DueDate,
                AttemptCount = invoice.AttemptCount,
                HostedInvoiceUrl = invoice.HostedInvoiceUrl,
                InvoicePdf = invoice.InvoicePdf
            };

            // Link to subscription
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
                "Created invoice {InvoiceId} with payment action required",
                dbInvoice.Id);
        }
        else
        {
            // Update existing invoice
            Logger.LogInformation(
                "Invoice {InvoiceId} requires payment action (3D Secure)",
                dbInvoice.Id);
        }

        await Db.SaveChangesAsync(ct);

        // 3. Get payment intent for action URL
        var actionUrl = invoice.HostedInvoiceUrl;

        // if (!string.IsNullOrWhiteSpace(invoice.PaymentIntentId))
        // {
        //     try
        //     {
        //         var paymentIntentService = new Stripe.PaymentIntentService();
        //         var paymentIntent = await paymentIntentService.GetAsync(
        //             invoice.PaymentIntentId,
        //             cancellationToken: ct);
        //         
        //         // Get the URL for 3D Secure authentication
        //         actionUrl = paymentIntent?.NextAction?.RedirectToUrl?.Url 
        //             ?? invoice.HostedInvoiceUrl;
        //     }
        //     catch (Exception ex)
        //     {
        //         Logger.LogWarning(ex,
        //             "Could not retrieve payment intent {PaymentIntentId} for action URL",
        //             invoice.PaymentIntentId);
        //     }
        // }

        // 4. Send email with authentication link
        if (!string.IsNullOrWhiteSpace(actionUrl))
        {
            BackgroundJob.Enqueue(() =>
                _emailService.SendPaymentActionRequiredEmailAsync(
                    customer.Id,
                    dbInvoice.Id,
                    actionUrl,
                    CancellationToken.None));

            Logger.LogInformation(
                "Queued payment action required email for customer {CustomerId}, invoice {InvoiceId}",
                customer.Id, dbInvoice.Id);
        }
        else
        {
            Logger.LogWarning(
                "No action URL available for invoice {InvoiceId}",
                dbInvoice.Id);
        }

        return new WebhookHandlerResult(
            true,
            $"Payment action required for invoice {dbInvoice.Id}",
            Metadata: new Dictionary<string, object>
            {
                ["invoice_id"] = dbInvoice.Id,
                ["customer_id"] = customer.Id,
                ["action_url"] = actionUrl ?? "N/A"
                // ["payment_intent_id"] = invoice.PaymentIntentId ?? "N/A"
            });
    }
}