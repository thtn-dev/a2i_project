// src/A2I.Infrastructure/StripeServices/WebhookHandlers/InvoiceVoidedHandler.cs

using A2I.Application.StripeAbstraction.Webhooks;
using A2I.Core.Enums;
using A2I.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Stripe;

namespace A2I.Infrastructure.StripeServices.WebhookHandlers;

public class InvoiceVoidedHandler : WebhookEventHandlerBase
{
    public InvoiceVoidedHandler(
        ApplicationDbContext db,
        ILogger<InvoiceVoidedHandler> logger)
        : base(db, logger)
    {
    }
    
    public override string EventType => EventTypes.InvoiceVoided;
    
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
                "Invoice {StripeInvoiceId} not found in DB",
                invoice.Id);
            
            return new WebhookHandlerResult(
                true,
                "Invoice not found in DB (might not have been synced)");
        }
        
        // Update to voided status
        dbInvoice.Status = InvoiceStatus.Void;
        dbInvoice.AmountDue = 0; // Voided invoices have no amount due
        
        await Db.SaveChangesAsync(ct);
        
        Logger.LogInformation(
            "Invoice {InvoiceId} voided",
            dbInvoice.Id);
        
        return new WebhookHandlerResult(
            true,
            $"Invoice voided: {dbInvoice.Id}",
            Metadata: new Dictionary<string, object>
            {
                ["invoice_id"] = dbInvoice.Id,
                ["invoice_number"] = dbInvoice.InvoiceNumber ?? "N/A"
            });
    }
}