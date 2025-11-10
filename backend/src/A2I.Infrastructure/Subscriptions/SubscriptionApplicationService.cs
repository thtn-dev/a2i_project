using System.Text.Json;
using A2I.Application.Common;
using A2I.Application.Notifications;
using A2I.Application.StripeAbstraction.Checkout;
using A2I.Application.StripeAbstraction.Subscriptions;
using A2I.Application.Subscriptions;
using A2I.Core.Entities;
using A2I.Core.Enums;
using A2I.Infrastructure.Database;
using BuildingBlocks.Utils.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace A2I.Infrastructure.Subscriptions;

public sealed class SubscriptionApplicationService(
    ApplicationDbContext db,
    IStripeCheckoutService checkoutService,
    IStripeSubscriptionService subscriptionService,
    IEmailService emailService,
    ILogger<SubscriptionApplicationService> logger)
    : ISubscriptionApplicationService
{
    public async Task<Result<StartSubscriptionResponse>> StartSubscriptionAsync(
        StartSubscriptionRequest request,
        CancellationToken ct = default)
    {
        var customer = await db.Customers
            .FirstOrDefaultAsync(c => c.Id == request.CustomerId, ct);

        if (customer is null)
            return Errors.NotFound($"Customer not found: {request.CustomerId}");

        var plan = await db.Plans
            .FirstOrDefaultAsync(p => p.Id == request.PlanId && p.IsActive == true, ct);

        if (plan is null)
            return Errors.NotFound($"Plan not found or inactive: {request.PlanId}");

        var hasActive = await db.Subscriptions
            .AnyAsync(
                s => s.CustomerId == request.CustomerId &&
                     (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Trialing), ct);

        if (hasActive)
            return Errors.Conflict("Customer already has an active subscription");

        if (string.IsNullOrWhiteSpace(customer.StripeCustomerId))
            return Errors.Validation("Customer does not have a Stripe customer ID");

        var checkoutRequest = new CreateCheckoutRequest
        {
            PriceId = plan.StripePriceId,
            Quantity = 1,
            CustomerId = customer.StripeCustomerId,
            SuccessUrl = request.SuccessUrl,
            CancelUrl = request.CancelUrl,
            AllowPromotionCodes = request.AllowPromotionCodes,
            TrialPeriodDays = plan.TrialPeriodDays,
            Metadata = request.Metadata ?? new Dictionary<string, string>()
        };

        checkoutRequest.Metadata["customer_id"] = customer.Id.ToString();
        checkoutRequest.Metadata["plan_id"] = plan.Id.ToString();

        var session = await checkoutService.CreateCheckoutSessionAsync(checkoutRequest, ct);

        logger.LogInformation(
            "Created checkout session {SessionId} for customer {CustomerId} plan {PlanId}",
            session.Id, customer.Id, plan.Id);

        return Result.Ok(new StartSubscriptionResponse
        {
            CheckoutSessionId = session.Id,
            CheckoutUrl = session.Url ?? string.Empty,
            ExpiresAt = session.ExpiresAt ?? DateTime.UtcNow.AddHours(24)
        });
    }

    public async Task<Result<SubscriptionDetailsResponse>> CompleteCheckoutAsync(
        string checkoutSessionId,
        CancellationToken ct = default)
    {
        var session = await checkoutService.GetCheckoutSessionAsync(checkoutSessionId, ct);

        if (session is null)
            return Errors.NotFound($"Checkout session not found: {checkoutSessionId}");

        if (session.Status != "complete")
            return Errors.Validation($"Checkout session not completed. Status: {session.Status}");

        if (string.IsNullOrWhiteSpace(session.SubscriptionId))
            return Errors.Validation("Checkout session has no subscription ID");

        var existing = await db.Subscriptions
            .FirstOrDefaultAsync(s => s.StripeSubscriptionId == session.SubscriptionId, ct);

        if (existing is not null)
        {
            logger.LogInformation(
                "Subscription already exists for session {SessionId}: {SubId}",
                checkoutSessionId, existing.Id);
            return await MapToDetailsResponse(existing, ct);
        }

        var stripeSub = await subscriptionService.GetSubscriptionAsync(session.SubscriptionId, ct);

        if (stripeSub is null)
            return Errors.ExternalService($"Stripe subscription not found: {session.SubscriptionId}");

        if (!stripeSub.Metadata.TryGetValue("customer_id", out var customerIdStr) ||
            !Guid.TryParse(customerIdStr, out var customerId))
            return Errors.Validation("Missing or invalid customer_id in metadata");

        if (!stripeSub.Metadata.TryGetValue("plan_id", out var planIdStr) ||
            !Guid.TryParse(planIdStr, out var planId))
            return Errors.Validation("Missing or invalid plan_id in metadata");

        var customer = await db.Customers.FindAsync([customerId], ct);
        var plan = await db.Plans.FindAsync([planId], ct);

        if (customer is null)
            return Errors.NotFound($"Customer not found: {customerId}");

        if (plan is null)
            return Errors.NotFound($"Plan not found: {planId}");

        var subscription = new Subscription
        {
            Id = IdGenHelper.NewGuidId(),
            CustomerId = customerId,
            PlanId = planId,
            StripeSubscriptionId = stripeSub.Id,
            Status = MapSubscriptionStatus(stripeSub.Status),
            CurrentPeriodStart = stripeSub.CurrentPeriodStart ?? DateTime.UtcNow,
            CurrentPeriodEnd = stripeSub.CurrentPeriodEnd ?? DateTime.UtcNow.AddMonths(1),
            CancelAt = stripeSub.CancelAt,
            CanceledAt = stripeSub.CanceledAt,
            CancelAtPeriodEnd = stripeSub.CancelAtPeriodEnd,
            TrialStart = stripeSub.TrialStart,
            TrialEnd = stripeSub.TrialEnd,
            Quantity = (int)stripeSub.Quantity,
            Metadata = stripeSub.Metadata is not null ? JsonSerializer.Serialize(stripeSub.Metadata) : null
        };

        db.Subscriptions.Add(subscription);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Created subscription {SubId} from checkout {SessionId}",
            subscription.Id, checkoutSessionId);

        return await MapToDetailsResponse(subscription, ct);
    }

    public async Task<Result<CancelSubscriptionResponse>> CancelSubscriptionAsync(
        Guid customerId,
        CancelSubscriptionRequest request,
        CancellationToken ct = default)
    {
        var subscription = await db.Subscriptions
            .Include(s => s.Plan)
            .FirstOrDefaultAsync(s => s.CustomerId == customerId && s.IsActive, ct);

        if (subscription is null)
            return Errors.NotFound("No active subscription found for this customer");

        var canceled = await subscriptionService.CancelSubscriptionAsync(
            subscription.StripeSubscriptionId,
            request.CancelImmediately,
            ct);

        subscription.Status = MapSubscriptionStatus(canceled.Status);
        subscription.CancelAtPeriodEnd = canceled.CancelAtPeriodEnd;
        subscription.CancelAt = canceled.CancelAt;
        subscription.CanceledAt = canceled.CanceledAt;

        if (!string.IsNullOrWhiteSpace(request.Reason))
        {
            var metadata = string.IsNullOrWhiteSpace(subscription.Metadata)
                ? new Dictionary<string, string>()
                : JsonSerializer.Deserialize<Dictionary<string, string>>(subscription.Metadata) ??
                  new Dictionary<string, string>();

            metadata["cancel_reason"] = request.Reason;
            subscription.Metadata = JsonSerializer.Serialize(metadata);
        }

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Canceled subscription {SubId} for customer {CustomerId}. Immediately={Immediately}",
            subscription.Id, customerId, request.CancelImmediately);

        var message = request.CancelImmediately
            ? "Subscription canceled immediately"
            : $"Subscription will be canceled at period end: {subscription.CurrentPeriodEnd:yyyy-MM-dd}";

        return Result.Ok(new CancelSubscriptionResponse
        {
            SubscriptionId = subscription.Id,
            Status = subscription.Status.ToString(),
            CancelAt = subscription.CancelAt,
            CanceledAt = subscription.CanceledAt,
            CancelAtPeriodEnd = subscription.CancelAtPeriodEnd,
            Message = message
        });
    }

    public async Task<Result<UpgradeSubscriptionResponse>> UpgradeSubscriptionAsync(
        Guid customerId,
        UpgradeSubscriptionRequest request,
        CancellationToken ct = default)
    {
        var subscription = await db.Subscriptions
            .Include(s => s.Plan)
            .FirstOrDefaultAsync(s => s.CustomerId == customerId && s.IsActive, ct);

        if (subscription is null)
            return Errors.NotFound("No active subscription found for this customer");

        var newPlan = await db.Plans
            .FirstOrDefaultAsync(p => p.Id == request.NewPlanId && p.IsActive, ct);

        if (newPlan is null)
            return Errors.NotFound($"Plan not found or inactive: {request.NewPlanId}");

        if (subscription.PlanId == request.NewPlanId)
            return Errors.Validation("Customer is already on this plan");

        if (subscription.IsInTrial && newPlan.Amount < subscription.Plan.Amount)
            return Errors.Validation("Cannot downgrade plan during trial period");

        var updated = await subscriptionService.ChangeSubscriptionPlanAsync(
            subscription.StripeSubscriptionId,
            newPlan.StripePriceId,
            ct);

        var prorationAmount = request.ApplyProration
            ? CalculateProration(subscription, newPlan)
            : 0m;

        var oldPlanId = subscription.PlanId;
        subscription.PlanId = request.NewPlanId;
        subscription.Status = MapSubscriptionStatus(updated.Status);

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Upgraded subscription {SubId} from plan {OldPlanId} to {NewPlanId}",
            subscription.Id, oldPlanId, request.NewPlanId);

        return Result.Ok(new UpgradeSubscriptionResponse
        {
            SubscriptionId = subscription.Id,
            OldPlanId = oldPlanId,
            NewPlanId = request.NewPlanId,
            NewPlanName = newPlan.Name,
            ProrationAmount = prorationAmount,
            Status = subscription.Status.ToString(),
            EffectiveDate = DateTime.UtcNow,
            Message = $"Successfully upgraded to {newPlan.Name}"
        });
    }

    public async Task<Result<SubscriptionDetailsResponse>> GetCustomerSubscriptionAsync(
        Guid customerId,
        CancellationToken ct = default)
    {
        var subscription = await db.Subscriptions
            .Include(s => s.Plan)
            .FirstOrDefaultAsync(s => s.CustomerId == customerId && !s.IsDeleted, ct);

        if (subscription is null)
            return Errors.NotFound($"No subscription found for customer {customerId}");

        return await MapToDetailsResponse(subscription, ct);
    }

    private async Task<Result<SubscriptionDetailsResponse>> MapToDetailsResponse(
        Subscription subscription,
        CancellationToken ct)
    {
        if (subscription.Plan is null)
        {
            subscription.Plan = await db.Plans.FindAsync([subscription.PlanId], ct);
            
            if (subscription.Plan is null)
                return Errors.NotFound($"Plan not found: {subscription.PlanId}");
        }

        var features = string.IsNullOrWhiteSpace(subscription.Plan.Features)
            ? null
            : JsonSerializer.Deserialize<List<string>>(subscription.Plan.Features);

        return Result.Ok(new SubscriptionDetailsResponse
        {
            Id = subscription.Id,
            CustomerId = subscription.CustomerId,
            PlanId = subscription.PlanId,
            StripeSubscriptionId = subscription.StripeSubscriptionId,
            Status = subscription.Status.ToString(),
            CurrentPeriodStart = subscription.CurrentPeriodStart,
            CurrentPeriodEnd = subscription.CurrentPeriodEnd,
            CancelAt = subscription.CancelAt,
            CanceledAt = subscription.CanceledAt,
            CancelAtPeriodEnd = subscription.CancelAtPeriodEnd,
            TrialStart = subscription.TrialStart,
            TrialEnd = subscription.TrialEnd,
            IsInTrial = subscription.IsInTrial,
            Quantity = subscription.Quantity,
            DaysUntilRenewal = subscription.DaysUntilRenewal,
            Plan = new PlanDetailsDto
            {
                Id = subscription.Plan.Id,
                Name = subscription.Plan.Name,
                Description = subscription.Plan.Description,
                Amount = subscription.Plan.Amount,
                Currency = subscription.Plan.Currency,
                BillingInterval = subscription.Plan.BillingInterval.ToString(),
                TrialPeriodDays = subscription.Plan.TrialPeriodDays,
                Features = features
            }
        });
    }

    private static SubscriptionStatus MapSubscriptionStatus(string? stripeStatus)
    {
        return stripeStatus?.ToLowerInvariant() switch
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

    private static decimal CalculateProration(Subscription current, Plan newPlan)
    {
        var daysRemaining = (current.CurrentPeriodEnd - DateTime.UtcNow).TotalDays;
        var totalDays = (current.CurrentPeriodEnd - current.CurrentPeriodStart).TotalDays;

        if (totalDays <= 0) return 0m;

        var unusedAmount = current.Plan.Amount * (decimal)(daysRemaining / totalDays);
        var newPeriodAmount = newPlan.Amount * (decimal)(daysRemaining / totalDays);

        return newPeriodAmount - unusedAmount;
    }
}