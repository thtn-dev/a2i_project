// src/A2I.Infrastructure/StripeServices/WebhookHandlers/InvoiceFinalizedHandler.cs

using A2I.Application.StripeAbstraction.Webhooks;
using A2I.Core.Enums;
using A2I.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Stripe;

namespace A2I.Infrastructure.StripeServices.WebhookHandlers;

public class InvoiceFinalizedHandler : WebhookEventHandlerBase
{
    public InvoiceFinalizedHandler(
        ApplicationDbContext db,
        ILogger<InvoiceFinalizedHandler> logger)
        : base(db, logger)
    {
    }
    
    public override string EventType => EventTypes.InvoiceFinalized;
    
    protected override async Task<WebhookHandlerResult> HandleCoreAsync(
        Event stripeEvent,
        CancellationToken ct)
    {
        var invoice = stripeEvent.Data.Object as Invoice;
        if (invoice == null)
        {
            return new WebhookHandlerResult(false, "Invalid invoice data");
        }
        
        // Find invoice in DB
        var dbInvoice = await Db.Invoices
            .FirstOrDefaultAsync(i => i.StripeInvoiceId == invoice.Id, ct);
        
        if (dbInvoice == null)
        {
            Logger.LogWarning(
                "Invoice {StripeInvoiceId} not found in DB (creating it)",
                invoice.Id);
            
            // Invoice might not exist yet - create it
            // (reuse logic from InvoiceCreatedHandler)
            return await CreateInvoiceFromStripe(invoice, ct);
        }
        
        // Update invoice to finalized status
        dbInvoice.Status = InvoiceStatus.Open; // Finalized = Open/ready for payment
        dbInvoice.InvoiceNumber = invoice.Number; // Number is assigned on finalization
        dbInvoice.HostedInvoiceUrl = invoice.HostedInvoiceUrl;
        dbInvoice.InvoicePdf = invoice.InvoicePdf;
        
        await Db.SaveChangesAsync(ct);
        
        Logger.LogInformation(
            "Invoice {InvoiceId} finalized: {InvoiceNumber}",
            dbInvoice.Id, invoice.Number);
        
        return new WebhookHandlerResult(
            true,
            $"Invoice finalized: {dbInvoice.Id}",
            Metadata: new Dictionary<string, object>
            {
                ["invoice_id"] = dbInvoice.Id,
                ["invoice_number"] = invoice.Number ?? "N/A",
                ["hosted_url"] = invoice.HostedInvoiceUrl ?? "N/A"
            });
    }
    
    private async Task<WebhookHandlerResult> CreateInvoiceFromStripe(
        Invoice invoice,
        CancellationToken ct)
    {
        var customer = await Db.Customers
            .FirstOrDefaultAsync(c => c.StripeCustomerId == invoice.CustomerId, ct);
        
        if (customer == null)
        {
            return new WebhookHandlerResult(
                false,
                "Customer not found",
                RequiresRetry: true);
        }
        
        var dbInvoice = new Core.Entities.Invoice
        {
            Id = Guid.NewGuid(),
            CustomerId = customer.Id,
            StripeInvoiceId = invoice.Id,
            StripePaymentIntentId = "",
            InvoiceNumber = invoice.Number,
            Status = InvoiceStatus.Open,
            Amount = invoice.AmountDue / 100m,
            AmountPaid = invoice.AmountPaid / 100m,
            AmountDue = invoice.AmountRemaining / 100m,
            Currency = invoice.Currency ?? "usd",
            PeriodStart = invoice.PeriodStart,
            PeriodEnd = invoice.PeriodEnd,
            DueDate = invoice.DueDate,
            HostedInvoiceUrl = invoice.HostedInvoiceUrl,
            InvoicePdf = invoice.InvoicePdf
        };
        
        // if (!string.IsNullOrWhiteSpace(invoice.SubscriptionId))
        // {
        //     var subscription = await Db.Subscriptions
        //         .FirstOrDefaultAsync(
        //             s => s.StripeSubscriptionId == invoice.SubscriptionId,
        //             ct);
        //     
        //     if (subscription != null)
        //     {
        //         dbInvoice.SubscriptionId = subscription.Id;
        //     }
        // }
        
        Db.Invoices.Add(dbInvoice);
        await Db.SaveChangesAsync(ct);
        
        Logger.LogInformation(
            "Created invoice {InvoiceId} from finalized event",
            dbInvoice.Id);
        
        return new WebhookHandlerResult(
            true,
            $"Invoice created and finalized: {dbInvoice.Id}");
    }
}