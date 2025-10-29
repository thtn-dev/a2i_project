using System.Text.Json;
using A2I.Application.StripeAbstraction.Webhooks;
using A2I.Core.Enums;
using A2I.Infrastructure.Database;
using BuildingBlocks.Utils.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Stripe;

namespace A2I.Infrastructure.StripeServices.WebhookHandlers;

public class SubscriptionUpdatedHandler : WebhookEventHandlerBase
{
    public SubscriptionUpdatedHandler(
        ApplicationDbContext db,
        ILogger<SubscriptionUpdatedHandler> logger)
        : base(db, logger)
    {
    }

    public override string EventType => EventTypes.CustomerSubscriptionUpdated;

    protected override async Task<WebhookHandlerResult> HandleCoreAsync(
        Event stripeEvent,
        CancellationToken ct)
    {
        var subscription = stripeEvent.Data.Object as Subscription;
        if (subscription == null) return new WebhookHandlerResult(false, "Invalid subscription data");

        var dbSubscription = await Db.Subscriptions
            .Include(s => s.Plan)
            .FirstOrDefaultAsync(
                s => s.StripeSubscriptionId == subscription.Id,
                ct);

        if (dbSubscription == null)
        {
            Logger.LogWarning(
                "Subscription {StripeSubId} not found in DB, creating it",
                subscription.Id);

            // This can happen if subscription was created directly via API
            // We'll create it here
            return await CreateSubscriptionFromStripe(subscription, ct);
        }

        // Track changes for logging
        var changes = new List<string>();
        var oldStatus = dbSubscription.Status;
        var newStatus = MapSubscriptionStatus(subscription.Status);

        // Update all fields
        dbSubscription.Status = newStatus;
        dbSubscription.CurrentPeriodStart = subscription.StartDate;
        dbSubscription.CurrentPeriodEnd = subscription.EndedAt ?? subscription.StartDate;
        dbSubscription.CancelAt = subscription.CancelAt;
        dbSubscription.CanceledAt = subscription.CanceledAt;
        dbSubscription.CancelAtPeriodEnd = subscription.CancelAtPeriodEnd;
        dbSubscription.TrialStart = subscription.TrialStart;
        dbSubscription.TrialEnd = subscription.TrialEnd;
        dbSubscription.EndedAt = subscription.EndedAt;
        dbSubscription.Quantity = (int)(subscription.Items?.Data?.FirstOrDefault()?.Quantity ?? 1);

        if (subscription.Metadata != null) dbSubscription.Metadata = JsonSerializer.Serialize(subscription.Metadata);

        // Detect and log critical changes
        if (oldStatus != newStatus)
        {
            changes.Add($"Status: {oldStatus} → {newStatus}");

            Logger.LogInformation(
                "Subscription {SubId} status changed: {OldStatus} → {NewStatus}",
                dbSubscription.Id, oldStatus, newStatus);
        }

        if (dbSubscription.CancelAtPeriodEnd)
        {
            changes.Add("Will cancel at period end");

            Logger.LogWarning(
                "Subscription {SubId} is set to cancel at period end: {EndDate}",
                dbSubscription.Id, dbSubscription.CurrentPeriodEnd);

            // TODO: Queue cancellation warning email
        }

        // Check for plan change
        var currentPriceId = subscription.Items?.Data?.FirstOrDefault()?.Price?.Id;
        if (!string.IsNullOrWhiteSpace(currentPriceId) &&
            currentPriceId != dbSubscription.Plan.StripePriceId)
        {
            changes.Add($"Plan changed to price {currentPriceId}");

            Logger.LogInformation(
                "Subscription {SubId} plan changed from {OldPriceId} to {NewPriceId}",
                dbSubscription.Id, dbSubscription.Plan.StripePriceId, currentPriceId);

            // Find new plan
            var newPlan = await Db.Plans
                .FirstOrDefaultAsync(p => p.StripePriceId == currentPriceId, ct);

            if (newPlan != null)
            {
                dbSubscription.PlanId = newPlan.Id;
                changes.Add($"Updated to plan: {newPlan.Name}");
            }
        }

        await Db.SaveChangesAsync(ct);

        var changesSummary = changes.Any()
            ? string.Join("; ", changes)
            : "No critical changes";

        Logger.LogInformation(
            "Updated subscription {SubId}: {Changes}",
            dbSubscription.Id, changesSummary);

        return new WebhookHandlerResult(
            true,
            $"Subscription updated: {changesSummary}",
            Metadata: new Dictionary<string, object>
            {
                ["subscription_id"] = dbSubscription.Id,
                ["changes"] = changes
            });
    }

    private async Task<WebhookHandlerResult> CreateSubscriptionFromStripe(
        Subscription stripeSubscription,
        CancellationToken ct)
    {
        // Find customer by Stripe ID
        var customer = await Db.Customers
            .FirstOrDefaultAsync(
                c => c.StripeCustomerId == stripeSubscription.CustomerId,
                ct);

        if (customer == null)
            return new WebhookHandlerResult(
                false,
                "Customer not found for subscription",
                true);

        // Find plan by Stripe price ID
        var priceId = stripeSubscription.Items?.Data?.FirstOrDefault()?.Price?.Id;
        if (string.IsNullOrWhiteSpace(priceId)) return new WebhookHandlerResult(false, "Subscription has no price ID");

        var plan = await Db.Plans
            .FirstOrDefaultAsync(p => p.StripePriceId == priceId, ct);

        if (plan == null)
        {
            Logger.LogWarning(
                "Plan not found for price {PriceId}, cannot create subscription",
                priceId);

            return new WebhookHandlerResult(
                false,
                $"Plan not found for price {priceId}");
        }

        var newSubscription = new Core.Entities.Subscription
        {
            Id = IdGenHelper.NewGuidId(),
            CustomerId = customer.Id,
            PlanId = plan.Id,
            StripeSubscriptionId = stripeSubscription.Id,
            Status = MapSubscriptionStatus(stripeSubscription.Status),
            CurrentPeriodStart = stripeSubscription.StartDate,
            CurrentPeriodEnd = stripeSubscription.EndedAt ?? stripeSubscription.StartDate,
            CancelAt = stripeSubscription.CancelAt,
            CanceledAt = stripeSubscription.CanceledAt,
            CancelAtPeriodEnd = stripeSubscription.CancelAtPeriodEnd,
            TrialStart = stripeSubscription.TrialStart,
            TrialEnd = stripeSubscription.TrialEnd,
            EndedAt = stripeSubscription.EndedAt,
            Quantity = (int)(stripeSubscription.Items?.Data?.FirstOrDefault()?.Quantity ?? 1),
            Metadata = stripeSubscription.Metadata != null
                ? JsonSerializer.Serialize(stripeSubscription.Metadata)
                : null
        };

        Db.Subscriptions.Add(newSubscription);
        await Db.SaveChangesAsync(ct);

        Logger.LogInformation(
            "Created subscription {SubId} from Stripe subscription {StrCreated subscription {SubId} from Stripe subscription {StripeSubId}",
            newSubscription.Id, stripeSubscription.Id);

        return new WebhookHandlerResult(
            true,
            $"Subscription created from Stripe: {newSubscription.Id}",
            Metadata: new Dictionary<string, object>
            {
                ["subscription_id"] = newSubscription.Id,
                ["customer_id"] = customer.Id,
                ["plan_id"] = plan.Id
            });
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
            _ => SubscriptionStatus.Incomplete
        };
    }
}