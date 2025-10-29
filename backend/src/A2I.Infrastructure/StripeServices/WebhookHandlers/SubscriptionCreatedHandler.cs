// src/A2I.Infrastructure/StripeServices/WebhookHandlers/SubscriptionCreatedHandler.cs

using System.Text.Json;
using A2I.Application.Notifications;
using A2I.Application.StripeAbstraction.Webhooks;
using A2I.Core.Entities;
using A2I.Core.Enums;
using A2I.Infrastructure.Database;
using BuildingBlocks.Utils.Helpers;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Stripe;
using StripeSubscription = Stripe.Subscription;
using Subscription = A2I.Core.Entities.Subscription;

namespace A2I.Infrastructure.StripeServices.WebhookHandlers;

public class SubscriptionCreatedHandler : WebhookEventHandlerBase
{
    private readonly IEmailService _emailService;

    public SubscriptionCreatedHandler(
        ApplicationDbContext db,
        IEmailService emailService,
        ILogger<SubscriptionCreatedHandler> logger)
        : base(db, logger)
    {
        _emailService = emailService;
    }

    public override string EventType => EventTypes.CustomerSubscriptionCreated;

    protected override async Task<WebhookHandlerResult> HandleCoreAsync(
        Event stripeEvent,
        CancellationToken ct)
    {
        if (stripeEvent.Data.Object is not StripeSubscription subscription)
        {
            return new WebhookHandlerResult(false, "Invalid subscription data");
        }

        // Check if subscription already exists (idempotency)
        // This can happen if checkout.session.completed already created it
        var existingSubscription = await Db.Subscriptions
            .FirstOrDefaultAsync(
                s => s.StripeSubscriptionId == subscription.Id,
                ct);

        if (existingSubscription != null)
        {
            Logger.LogInformation(
                "Subscription {SubId} already exists (likely created via checkout.session.completed)",
                existingSubscription.Id);

            return new WebhookHandlerResult(
                true,
                $"Subscription already exists: {existingSubscription.Id}");
        }

        // Find customer by Stripe ID
        var customer = await Db.Customers
            .FirstOrDefaultAsync(
                c => c.StripeCustomerId == subscription.CustomerId,
                ct);

        if (customer == null)
        {
            Logger.LogError(
                "Customer not found for Stripe customer ID: {StripeCustomerId}",
                subscription.CustomerId);

            return new WebhookHandlerResult(
                false,
                "Customer not found",
                true);
        }

        // Find plan by Stripe price ID
        var priceId = subscription.Items?.Data?.FirstOrDefault()?.Price?.Id;
        if (string.IsNullOrWhiteSpace(priceId))
        {
            Logger.LogError(
                "Subscription {StripeSubId} has no price ID",
                subscription.Id);

            return new WebhookHandlerResult(false, "Subscription has no price ID");
        }

        var plan = await Db.Plans
            .FirstOrDefaultAsync(p => p.StripePriceId == priceId, ct);

        if (plan == null)
        {
            Logger.LogWarning(
                "Plan not found for price {PriceId}",
                priceId);

            return new WebhookHandlerResult(
                false,
                $"Plan not found for price {priceId}");
        }

        var currentPeriodEnd = plan.CalculateNextBillingDate(subscription.StartDate);
        var newSubscription = new Subscription
        {
            Id = IdGenHelper.NewGuidId(),
            CustomerId = customer.Id,
            PlanId = plan.Id,
            StripeSubscriptionId = subscription.Id,
            Status = MapSubscriptionStatus(subscription.Status),
            CurrentPeriodStart = subscription.StartDate,
            CurrentPeriodEnd = currentPeriodEnd,
            CancelAt = subscription.CancelAt,
            CanceledAt = subscription.CanceledAt,
            CancelAtPeriodEnd = subscription.CancelAtPeriodEnd,
            TrialStart = subscription.TrialStart,
            TrialEnd = subscription.TrialEnd,
            EndedAt = subscription.EndedAt,
            Quantity = (int)(subscription.Items?.Data?.FirstOrDefault()?.Quantity ?? 1),
            Metadata = subscription.Metadata != null
                ? JsonSerializer.Serialize(subscription.Metadata)
                : null
        };

        Db.Subscriptions.Add(newSubscription);
        await Db.SaveChangesAsync(ct);

        Logger.LogInformation(
            "Created subscription {SubId} for customer {CustomerId} via subscription.created event",
            newSubscription.Id, customer.Id);

        // Send welcome email if not in trial (trial welcome is sent at trial end)
        if (!newSubscription.IsInTrial)
            BackgroundJob.Enqueue(() =>
                _emailService.SendWelcomeEmailAsync(
                    customer.Id,
                    customer.Email,
                    plan.Name,
                    CancellationToken.None));

        return new WebhookHandlerResult(
            true,
            $"Subscription created: {newSubscription.Id}",
            Metadata: new Dictionary<string, object>
            {
                ["subscription_id"] = newSubscription.Id,
                ["customer_id"] = customer.Id,
                ["plan_id"] = plan.Id,
                ["is_trial"] = newSubscription.IsInTrial
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