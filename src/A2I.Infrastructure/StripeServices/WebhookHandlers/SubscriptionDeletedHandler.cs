// src/A2I.Infrastructure/StripeServices/WebhookHandlers/SubscriptionDeletedHandler.cs

using A2I.Application.Notifications;
using A2I.Application.StripeAbstraction.Webhooks;
using A2I.Core.Enums;
using A2I.Infrastructure.Database;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Stripe;

namespace A2I.Infrastructure.StripeServices.WebhookHandlers;

public class SubscriptionDeletedHandler : WebhookEventHandlerBase
{
    private readonly IEmailService _emailService;
    
    public SubscriptionDeletedHandler(
        ApplicationDbContext db,
        IEmailService emailService,
        ILogger<SubscriptionDeletedHandler> logger)
        : base(db, logger)
    {
        _emailService = emailService;
    }
    
    public override string EventType => EventTypes.CustomerSubscriptionDeleted;
    protected override async Task<WebhookHandlerResult> HandleCoreAsync(
        Event stripeEvent,
        CancellationToken ct)
    {
        var subscription = stripeEvent.Data.Object as Subscription;
        if (subscription == null)
        {
            return new WebhookHandlerResult(false, "Invalid subscription data");
        }
        
        // 1. Find subscription in DB
        var dbSubscription = await Db.Subscriptions
            .Include(s => s.Customer)
            .Include(s => s.Plan)
            .FirstOrDefaultAsync(
                s => s.StripeSubscriptionId == subscription.Id,
                ct);
        
        if (dbSubscription == null)
        {
            Logger.LogWarning(
                "Subscription {StripeSubId} not found in DB (already deleted or never created)",
                subscription.Id);
            
            return new WebhookHandlerResult(
                true,
                "Subscription not found in DB (might be already deleted)");
        }
        
        // Check if already deleted (idempotency)
        if (dbSubscription.IsDeleted)
        {
            Logger.LogInformation(
                "Subscription {SubId} already marked as deleted",
                dbSubscription.Id);
            
            return new WebhookHandlerResult(
                true,
                $"Subscription {dbSubscription.Id} already deleted");
        }
        
        // 2. Update subscription status
        dbSubscription.Status = SubscriptionStatus.Canceled;
        dbSubscription.CanceledAt = subscription.CanceledAt ?? DateTime.UtcNow;
        dbSubscription.EndedAt = subscription.EndedAt ?? DateTime.UtcNow;
        
        // 3. Soft delete (preserve data for analytics)
        dbSubscription.IsDeleted = true;
        dbSubscription.DeletedAt = DateTime.UtcNow;
        
        Logger.LogInformation(
            "Subscription {SubId} deleted/canceled. Customer: {CustomerId}, Plan: {PlanName}",
            dbSubscription.Id, dbSubscription.CustomerId, dbSubscription.Plan.Name);
        
        await Db.SaveChangesAsync(ct);
        
        // 4. Queue cancellation confirmation email
        BackgroundJob.Enqueue(() =>
            _emailService.SendCancellationEmailAsync(
                dbSubscription.CustomerId,
                dbSubscription.Plan.Name,
                dbSubscription.EndedAt ?? DateTime.UtcNow,
                CancellationToken.None));
        
        // 5. TODO: Revoke feature access
        // BackgroundJob.Enqueue(() => 
        //     _featureAccessManager.RevokeFeaturesAsync(dbSubscription.CustomerId));
        
        return new WebhookHandlerResult(
            true,
            $"Subscription {dbSubscription.Id} canceled and archived",
            Metadata: new Dictionary<string, object>
            {
                ["subscription_id"] = dbSubscription.Id,
                ["customer_id"] = dbSubscription.CustomerId,
                ["plan_name"] = dbSubscription.Plan.Name,
                ["ended_at"] = dbSubscription.EndedAt ?? DateTime.UtcNow
            });
    }
}