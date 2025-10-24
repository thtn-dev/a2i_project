// src/A2I.Infrastructure/StripeServices/WebhookHandlers/SubscriptionTrialWillEndHandler.cs

using A2I.Application.Notifications;
using A2I.Application.StripeAbstraction.Webhooks;
using A2I.Infrastructure.Database;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Stripe;

namespace A2I.Infrastructure.StripeServices.WebhookHandlers;

public class SubscriptionTrialWillEndHandler : WebhookEventHandlerBase
{
    private readonly IEmailService _emailService;
    
    public SubscriptionTrialWillEndHandler(
        ApplicationDbContext db,
        IEmailService emailService,
        ILogger<SubscriptionTrialWillEndHandler> logger)
        : base(db, logger)
    {
        _emailService = emailService;
    }

    public override string EventType => EventTypes.CustomerSubscriptionTrialWillEnd;
    
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
                "Subscription {StripeSubId} not found in DB",
                subscription.Id);
            
            return new WebhookHandlerResult(
                false,
                "Subscription not found",
                RequiresRetry: true);
        }
        
        // 2. Verify trial end date
        if (!subscription.TrialEnd.HasValue)
        {
            Logger.LogWarning(
                "Subscription {SubId} has no trial end date",
                dbSubscription.Id);
            
            return new WebhookHandlerResult(
                true,
                "Subscription has no trial end date");
        }
        
        var daysUntilTrialEnd = (subscription.TrialEnd.Value - DateTime.UtcNow).TotalDays;
        
        Logger.LogInformation(
            "Subscription {SubId} trial will end in {Days} days (on {TrialEndDate})",
            dbSubscription.Id,
            Math.Round(daysUntilTrialEnd, 1),
            subscription.TrialEnd.Value);
        
        // 3. Check if customer has payment method
        var hasPaymentMethod = false;
        if (!string.IsNullOrWhiteSpace(dbSubscription.Customer.StripeCustomerId))
        {
            try
            {
                var customerService = new CustomerService();
                var stripeCustomer = await customerService.GetAsync(
                    dbSubscription.Customer.StripeCustomerId,
                    cancellationToken: ct);
                
                hasPaymentMethod = !string.IsNullOrWhiteSpace(
                    stripeCustomer?.InvoiceSettings?.DefaultPaymentMethodId);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex,
                    "Could not check payment method for customer {CustomerId}",
                    dbSubscription.CustomerId);
            }
        }
        
        // 4. Send trial ending email
        BackgroundJob.Enqueue(() =>
            _emailService.SendTrialEndingEmailAsync(
                dbSubscription.CustomerId,
                dbSubscription.Plan.Name,
                subscription.TrialEnd.Value,
                CancellationToken.None));
        
        Logger.LogInformation(
            "Queued trial ending email for customer {CustomerId}. Has payment method: {HasPaymentMethod}",
            dbSubscription.CustomerId, hasPaymentMethod);
        
        return new WebhookHandlerResult(
            true,
            $"Trial ending reminder sent for subscription {dbSubscription.Id}",
            Metadata: new Dictionary<string, object>
            {
                ["subscription_id"] = dbSubscription.Id,
                ["customer_id"] = dbSubscription.CustomerId,
                ["trial_end_date"] = subscription.TrialEnd.Value,
                ["days_until_end"] = Math.Round(daysUntilTrialEnd, 1),
                ["has_payment_method"] = hasPaymentMethod
            });
    }
}