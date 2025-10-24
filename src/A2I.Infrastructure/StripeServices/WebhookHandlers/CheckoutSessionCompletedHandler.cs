using System.Text.Json;
using A2I.Application.Notifications;
using A2I.Application.StripeAbstraction.Webhooks;
using A2I.Core.Enums;
using A2I.Infrastructure.Database;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Stripe;
using Stripe.Checkout;
using Subscription = A2I.Core.Entities.Subscription;

namespace A2I.Infrastructure.StripeServices.WebhookHandlers;

public class CheckoutSessionCompletedHandler : WebhookEventHandlerBase
{
    private readonly IEmailService _emailService;
    
    public CheckoutSessionCompletedHandler(
        ApplicationDbContext db,
        IEmailService emailService,
        ILogger<CheckoutSessionCompletedHandler> logger)
        : base(db, logger)
    {
        _emailService = emailService;
    }

    public override string EventType => EventTypes.CheckoutSessionCompleted;
    
    protected override async Task<WebhookHandlerResult> HandleCoreAsync(
        Event stripeEvent,
        CancellationToken ct)
    {
        var session = stripeEvent.Data.Object as Session;
        if (session == null)
        {
            return new WebhookHandlerResult(false, "Invalid session data");
        }
        
        // 1. Verify session status
        if (session.Status != "complete")
        {
            Logger.LogWarning(
                "Checkout session {SessionId} is not complete: {Status}",
                session.Id, session.Status);
            
            return new WebhookHandlerResult(
                true,
                $"Session status is {session.Status}, not processing");
        }
        
        if (string.IsNullOrWhiteSpace(session.SubscriptionId))
        {
            Logger.LogWarning(
                "Checkout session {SessionId} has no subscription ID",
                session.Id);
            
            return new WebhookHandlerResult(
                true,
                "Session has no subscription (might be one-time payment)");
        }
        
        // 2. Extract metadata
        var metadata = session.Metadata ?? new Dictionary<string, string>();
        if (!metadata.TryGetValue("customer_id", out var customerIdStr) ||
            !Guid.TryParse(customerIdStr, out var customerId))
        {
            Logger.LogError(
                "Checkout session {SessionId} missing customer_id in metadata",
                session.Id);
            
            return new WebhookHandlerResult(false, "Missing customer_id in metadata");
        }
        
        if (!metadata.TryGetValue("plan_id", out var planIdStr) ||
            !Guid.TryParse(planIdStr, out var planId))
        {
            Logger.LogError(
                "Checkout session {SessionId} missing plan_id in metadata",
                session.Id);
            
            return new WebhookHandlerResult(false, "Missing plan_id in metadata");
        }
        
        // 3. Check if subscription already exists (idempotency)
        var existingSubscription = await Db.Subscriptions
            .FirstOrDefaultAsync(
                s => s.StripeSubscriptionId == session.SubscriptionId,
                ct);
        
        if (existingSubscription != null)
        {
            Logger.LogInformation(
                "Subscription {SubId} already exists for session {SessionId}",
                existingSubscription.Id, session.Id);
            
            // Just update status if needed
            if (existingSubscription.Status != SubscriptionStatus.Active &&
                existingSubscription.Status != SubscriptionStatus.Trialing)
            {
                existingSubscription.Status = DetermineStatus(session);
                await Db.SaveChangesAsync(ct);
            }
            
            return new WebhookHandlerResult(
                true,
                $"Subscription already exists: {existingSubscription.Id}");
        }
        
        // 4. Verify customer and plan exist
        var customer = await Db.Customers.FindAsync(new object[] { customerId }, ct);
        if (customer == null)
        {
            Logger.LogError("Customer {CustomerId} not found", customerId);
            return new WebhookHandlerResult(false, "Customer not found", RequiresRetry: true);
        }
        
        var plan = await Db.Plans.FindAsync(new object[] { planId }, ct);
        if (plan == null)
        {
            Logger.LogError("Plan {PlanId} not found", planId);
            return new WebhookHandlerResult(false, "Plan not found", RequiresRetry: true);
        }
        
        // 5. Get full subscription details from Stripe
        var subscriptionService = new SubscriptionService();
        var stripeSubscription = await subscriptionService.GetAsync(session.SubscriptionId, cancellationToken: ct);
        
        if (stripeSubscription == null)
        {
            Logger.LogError(
                "Could not retrieve subscription {SubId} from Stripe",
                session.SubscriptionId);
            
            return new WebhookHandlerResult(false, "Stripe subscription not found", RequiresRetry: true);
        }
        
        // 6. Create subscription in DB
        var subscription = new Subscription
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            PlanId = planId,
            StripeSubscriptionId = session.SubscriptionId,
            Status = MapSubscriptionStatus(stripeSubscription.Status),
            CurrentPeriodStart = stripeSubscription.StartDate,
            CurrentPeriodEnd = stripeSubscription.EndedAt ?? stripeSubscription.StartDate,
            CancelAt = stripeSubscription.CancelAt,
            CanceledAt = stripeSubscription.CanceledAt,
            CancelAtPeriodEnd = stripeSubscription.CancelAtPeriodEnd,
            TrialStart = stripeSubscription.TrialStart,
            TrialEnd = stripeSubscription.TrialEnd,
            Quantity = (int)(stripeSubscription.Items?.Data?.FirstOrDefault()?.Quantity ?? 1),
            Metadata = stripeSubscription.Metadata != null 
                ? JsonSerializer.Serialize(stripeSubscription.Metadata) 
                : null
        };
        
        Db.Subscriptions.Add(subscription);
        await Db.SaveChangesAsync(ct);
        
        Logger.LogInformation(
            "Created subscription {SubId} for customer {CustomerId} from checkout {SessionId}",
            subscription.Id, customerId, session.Id);
        
        // 7. Queue welcome email
        BackgroundJob.Enqueue(() => 
            _emailService.SendWelcomeEmailAsync(
                customerId,
                customer.Email,
                plan.Name,
                CancellationToken.None));
        
        return new WebhookHandlerResult(
            true,
            $"Subscription created: {subscription.Id}",
            Metadata: new Dictionary<string, object>
            {
                ["subscription_id"] = subscription.Id,
                ["customer_id"] = customerId,
                ["plan_id"] = planId
            });
    }
    
    private static SubscriptionStatus DetermineStatus(Session session)
    {
        // For completed checkout, subscription should be active or trialing
        var sub = session.Subscription;
        if (sub != null)
        {
            return MapSubscriptionStatus(sub.Status);
        }
        
        return SubscriptionStatus.Active;
    }
    
    private static SubscriptionStatus MapSubscriptionStatus(string? status)
    {
        return status?.ToLowerInvariant() switch
        {
            "incomplete" => SubscriptionStatus.Incomplete,
            "incomplete_expired" => SubscriptionStatus.IncompleteExpired,
            "trialing" => SubscriptionStatus.Trialing,
            "active" => SubscriptionStatus.Active,
            "past_due" => SubscriptionStatus.PastDue,
            "canceled" => SubscriptionStatus.Canceled,
            "unpaid" => SubscriptionStatus.Unpaid,
            "paused" => SubscriptionStatus.Paused,
            _ => SubscriptionStatus.Active
        };
    }
}