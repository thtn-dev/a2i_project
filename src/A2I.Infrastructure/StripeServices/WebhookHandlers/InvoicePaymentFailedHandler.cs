using System.Text.Json;
using A2I.Application.Notifications;
using A2I.Application.StripeAbstraction.Webhooks;
using A2I.Core.Entities;
using A2I.Core.Enums;
using A2I.Infrastructure.Database;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Stripe;
using Invoice = Stripe.Invoice;

namespace A2I.Infrastructure.StripeServices.WebhookHandlers;

public class InvoicePaymentFailedHandler : WebhookEventHandlerBase
{
    private readonly IEmailService _emailService;
    private readonly IConfiguration _config;
    private readonly int _gracePeriodDays;
    
    public InvoicePaymentFailedHandler(
        ApplicationDbContext db,
        IEmailService emailService,
        IConfiguration config,
        ILogger<InvoicePaymentFailedHandler> logger)
        : base(db, logger)
    {
        _emailService = emailService;
        _config = config;
        _gracePeriodDays = 7;
    }
    
    public override string EventType => EventTypes.InvoicePaymentFailed;
    
    protected override async Task<WebhookHandlerResult> HandleCoreAsync(
        Event stripeEvent,
        CancellationToken ct)
    {
        if (stripeEvent.Data.Object is not Invoice invoice)
        {
            return new WebhookHandlerResult(false, "Invalid invoice data");
        }
        
        // 1. Find customer
        var customer = await Db.Customers
            .FirstOrDefaultAsync(c => c.StripeCustomerId == invoice.CustomerId, ct);
        
        if (customer == null)
        {
            Logger.LogError(
                "Customer not found for Stripe customer ID: {StripeCustomerId}",
                invoice.CustomerId);
            
            return new WebhookHandlerResult(false, "Customer not found", RequiresRetry: true);
        }
        
        // 2. Find or create invoice
        var dbInvoice = await Db.Invoices
            .Include(i => i.Subscription)
            .FirstOrDefaultAsync(i => i.StripeInvoiceId == invoice.Id, ct);
        
        if (dbInvoice == null)
        {
            // Create invoice with failed status
            dbInvoice = new Core.Entities.Invoice
            {
                Id = Guid.NewGuid(),
                CustomerId = customer.Id,
                StripeInvoiceId = invoice.Id,
                StripePaymentIntentId = "",
                InvoiceNumber = invoice.Number,
                Status = InvoiceStatus.Open, // Stripe keeps it open for retry
                Amount = invoice.AmountDue / 100m,
                AmountPaid = invoice.AmountPaid / 100m,
                AmountDue = invoice.AmountRemaining / 100m,
                Currency = invoice.Currency ?? "usd",
                PeriodStart = invoice.PeriodStart,
                PeriodEnd = invoice.PeriodEnd,
                DueDate = invoice.DueDate,
                AttemptCount = invoice.AttemptCount ,
                LastAttemptAt = DateTime.UtcNow,
                NextAttemptAt = invoice.NextPaymentAttempt,
                HostedInvoiceUrl = invoice.HostedInvoiceUrl,
                InvoicePdf = invoice.InvoicePdf
            };
            
            // Link to subscription
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
        }
        else
        {
            // Update existing invoice
            dbInvoice.AttemptCount = invoice.AttemptCount;
            dbInvoice.LastAttemptAt = DateTime.UtcNow;
            dbInvoice.NextAttemptAt = invoice.NextPaymentAttempt;
            dbInvoice.AmountDue = invoice.AmountRemaining / 100m;
        }
        
        // 3. Track first failure date in metadata
        var metadata = string.IsNullOrWhiteSpace(dbInvoice.Metadata)
            ? new Dictionary<string, string>()
            : JsonSerializer.Deserialize<Dictionary<string, string>>(dbInvoice.Metadata) 
              ?? new Dictionary<string, string>();
        
        if (!metadata.ContainsKey("first_failure_date"))
        {
            metadata["first_failure_date"] = DateTime.UtcNow.ToString("O");
            dbInvoice.Metadata = JsonSerializer.Serialize(metadata);
        }
        
        // 4. Check grace period
        var firstFailureDate = metadata.TryGetValue("first_failure_date", out var dateStr)
            ? DateTime.Parse(dateStr)
            : DateTime.UtcNow;
        
        var daysSinceFirstFailure = (DateTime.UtcNow - firstFailureDate).TotalDays;
        var isInGracePeriod = daysSinceFirstFailure <= _gracePeriodDays;
        
        Logger.LogWarning(
            "Payment failed for invoice {InvoiceId} (attempt {Attempt}). Days since first failure: {Days}/{GraceDays}",
            dbInvoice.Id, dbInvoice.AttemptCount, (int)daysSinceFirstFailure, _gracePeriodDays);
        
        // 5. Update subscription status if applicable
        if (dbInvoice.SubscriptionId.HasValue)
        {
            var subscription = await Db.Subscriptions
                .FindAsync(new object[] { dbInvoice.SubscriptionId.Value }, ct);
            
            if (subscription != null)
            {
                if (!isInGracePeriod)
                {
                    // Grace period expired - mark as past_due
                    subscription.Status = SubscriptionStatus.PastDue;
                    
                    Logger.LogWarning(
                        "Subscription {SubId} marked as past_due (grace period expired)",
                        subscription.Id);
                    
                    // Queue aggressive dunning email
                    BackgroundJob.Enqueue(() =>
                        _emailService.SendPaymentFailedEmailAsync(
                            customer.Id,
                            dbInvoice.Id,
                            dbInvoice.AttemptCount,
                            dbInvoice.NextAttemptAt,
                            CancellationToken.None));
                }
                else
                {
                    // Still in grace period - keep active but warn
                    Logger.LogInformation(
                        "Subscription {SubId} payment failed but still in grace period ({Days}/{GraceDays} days)",
                        subscription.Id, (int)daysSinceFirstFailure, _gracePeriodDays);
                    
                    // Queue soft dunning email
                    BackgroundJob.Enqueue(() =>
                        _emailService.SendPaymentFailedEmailAsync(
                            customer.Id,
                            dbInvoice.Id,
                            dbInvoice.AttemptCount,
                            dbInvoice.NextAttemptAt,
                            CancellationToken.None));
                }
            }
        }
        
        await Db.SaveChangesAsync(ct);
        
        return new WebhookHandlerResult(
            true,
            $"Payment failure recorded for invoice {dbInvoice.Id}",
            Metadata: new Dictionary<string, object>
            {
                ["invoice_id"] = dbInvoice.Id,
                ["customer_id"] = customer.Id,
                ["attempt_count"] = dbInvoice.AttemptCount,
                ["in_grace_period"] = isInGracePeriod,
                ["days_since_first_failure"] = (int)daysSinceFirstFailure
            });
    }
}