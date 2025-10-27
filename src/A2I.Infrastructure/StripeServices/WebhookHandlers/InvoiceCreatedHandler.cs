// src/A2I.Infrastructure/StripeServices/WebhookHandlers/InvoiceCreatedHandler.cs

using A2I.Application.StripeAbstraction.Webhooks;
using A2I.Core.Enums;
using A2I.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Stripe;
using Invoice = A2I.Core.Entities.Invoice;

namespace A2I.Infrastructure.StripeServices.WebhookHandlers;

public class InvoiceCreatedHandler : WebhookEventHandlerBase
{
    public InvoiceCreatedHandler(
        ApplicationDbContext db,
        ILogger<InvoiceCreatedHandler> logger)
        : base(db, logger)
    {
    }

    public override string EventType => EventTypes.InvoiceCreated;

    protected override async Task<WebhookHandlerResult> HandleCoreAsync(
        Event stripeEvent,
        CancellationToken ct)
    {
        if (stripeEvent.Data.Object is not Stripe.Invoice invoice)
        {
            return new WebhookHandlerResult(false, "Invalid invoice data");
        }

        // Skip draft invoices
        if (invoice.Status == "draft")
        {
            Logger.LogDebug(
                "Skipping draft invoice {StripeInvoiceId}",
                invoice.Id);

            return new WebhookHandlerResult(
                true,
                "Draft invoice ignored (will process when finalized)");
        }

        var existingInvoice = await Db.Invoices
            .FirstOrDefaultAsync(i => i.StripeInvoiceId == invoice.Id, ct);

        if (existingInvoice != null)
        {
            Logger.LogInformation(
                "Invoice {InvoiceId} already exists",
                existingInvoice.Id);

            return new WebhookHandlerResult(
                true,
                $"Invoice already exists: {existingInvoice.Id}");
        }

        var customer = await Db.Customers
            .FirstOrDefaultAsync(c => c.StripeCustomerId == invoice.CustomerId, ct);

        if (customer == null)
        {
            Logger.LogError(
                "Customer not found for invoice {StripeInvoiceId}",
                invoice.Id);

            return new WebhookHandlerResult(
                false,
                "Customer not found",
                true);
        }

        var dbInvoice = new Invoice
        {
            Id = Guid.NewGuid(),
            CustomerId = customer.Id,
            StripeInvoiceId = invoice.Id,
            StripePaymentIntentId = "",
            InvoiceNumber = invoice.Number,
            Status = MapInvoiceStatus(invoice.Status),
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
        await Db.SaveChangesAsync(ct);

        Logger.LogInformation(
            "Created invoice {InvoiceId} ({Status}) for customer {CustomerId}",
            dbInvoice.Id, invoice.Status, customer.Id);

        return new WebhookHandlerResult(
            true,
            $"Invoice created: {dbInvoice.Id}",
            Metadata: new Dictionary<string, object>
            {
                ["invoice_id"] = dbInvoice.Id,
                ["customer_id"] = customer.Id,
                ["status"] = invoice.Status ?? "unknown"
            });
    }

    private static InvoiceStatus MapInvoiceStatus(string? status)
    {
        return status?.ToLowerInvariant() switch
        {
            "draft" => InvoiceStatus.Draft,
            "open" => InvoiceStatus.Open,
            "paid" => InvoiceStatus.Paid,
            "uncollectible" => InvoiceStatus.Uncollectible,
            "void" => InvoiceStatus.Void,
            _ => InvoiceStatus.Draft
        };
    }
}