using System.Text.Json;
using A2I.Application.Notifications;
using A2I.Application.StripeAbstraction;
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

public sealed class SubscriptionApplicationService : ISubscriptionApplicationService
{
    private readonly IStripeCheckoutService _checkoutService;
    private readonly ApplicationDbContext _db;
    private readonly ILogger<SubscriptionApplicationService> _logger;
    private readonly IStripeSubscriptionService _subscriptionService;
    private readonly IEmailService _emailService;

    public SubscriptionApplicationService(
        ApplicationDbContext db,
        IStripeCheckoutService checkoutService,
        IStripeSubscriptionService subscriptionService,
        IEmailService emailService,
        ILogger<SubscriptionApplicationService> logger)
    {
        _db = db;
        _checkoutService = checkoutService;
        _subscriptionService = subscriptionService;
        _emailService = emailService;
        _logger = logger;
    }


    public async Task<StartSubscriptionResponse> StartSubscriptionAsync(
        StartSubscriptionRequest request,
        CancellationToken ct = default)
    {
        var customer = await _db.Customers
            .FirstOrDefaultAsync(c => c.Id == request.CustomerId, ct);

        if (customer is null)
            throw new BusinessException($"Customer not found: {request.CustomerId}");

        var plan = await _db.Plans
            .FirstOrDefaultAsync(p => p.Id == request.PlanId && p.IsActive == true, ct);

        if (plan is null)
            throw new BusinessException($"Plan not found or inactive: {request.PlanId}");

        var hasActive = await _db.Subscriptions
            .AnyAsync(
                s => s.CustomerId == request.CustomerId &&
                     (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Trialing), ct);

        if (hasActive)
            throw new BusinessException("Customer already has an active subscription");

        if (string.IsNullOrWhiteSpace(customer.StripeCustomerId))
            throw new BusinessException("Customer does not have a Stripe customer ID");

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

        var session = await _checkoutService.CreateCheckoutSessionAsync(checkoutRequest, ct);

        _logger.LogInformation(
            "Created checkout session {SessionId} for customer {CustomerId} plan {PlanId}",
            session.Id, customer.Id, plan.Id);

        return new StartSubscriptionResponse
        {
            CheckoutSessionId = session.Id,
            CheckoutUrl = session.Url ?? string.Empty,
            ExpiresAt = session.ExpiresAt ?? DateTime.UtcNow.AddHours(24)
        };
    }


    public async Task<SubscriptionDetailsResponse> CompleteCheckoutAsync(
        string checkoutSessionId,
        CancellationToken ct = default)
    {
        var session = await _checkoutService.GetCheckoutSessionAsync(checkoutSessionId, ct);

        if (session is null)
            throw new BusinessException($"Checkout session not found: {checkoutSessionId}");

        if (session.Status != "complete")
            throw new BusinessException($"Checkout session not completed. Status: {session.Status}");

        if (string.IsNullOrWhiteSpace(session.SubscriptionId))
            throw new BusinessException("Checkout session has no subscription ID");

        var existing = await _db.Subscriptions
            .FirstOrDefaultAsync(s => s.StripeSubscriptionId == session.SubscriptionId, ct);

        if (existing is not null)
        {
            _logger.LogInformation(
                "Subscription already exists for session {SessionId}: {SubId}",
                checkoutSessionId, existing.Id);
            return await MapToDetailsResponse(existing, ct);
        }

        var stripeSub = await _subscriptionService.GetSubscriptionAsync(session.SubscriptionId, ct);

        if (stripeSub is null)
            throw new BusinessException($"Stripe subscription not found: {session.SubscriptionId}");

        var customerId = Guid.Parse(stripeSub.Metadata?["customer_id"] ??
                                    throw new BusinessException("Missing customer_id in metadata"));
        var planId = Guid.Parse(stripeSub.Metadata?["plan_id"] ??
                                throw new BusinessException("Missing plan_id in metadata"));

        var customer = await _db.Customers.FindAsync([customerId], ct);
        var plan = await _db.Plans.FindAsync([planId], ct);

        if (customer is null || plan is null)
            throw new BusinessException("Customer or Plan not found");

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

        _db.Subscriptions.Add(subscription);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Created subscription {SubId} from checkout {SessionId}",
            subscription.Id, checkoutSessionId);

        return await MapToDetailsResponse(subscription, ct);
    }

    // ==================== CANCEL SUBSCRIPTION ====================

    public async Task<CancelSubscriptionResponse> CancelSubscriptionAsync(
        Guid customerId,
        CancelSubscriptionRequest request,
        CancellationToken ct = default)
    {
        var subscription = await _db.Subscriptions
            .Include(s => s.Plan)
            .FirstOrDefaultAsync(s => s.CustomerId == customerId && s.IsActive, ct);

        if (subscription is null)
            throw new BusinessException("No active subscription found for this customer");

        // Cancel on Stripe (NO REFUND policy)
        var canceled = await _subscriptionService.CancelSubscriptionAsync(
            subscription.StripeSubscriptionId,
            request.CancelImmediately,
            ct);

        // Update DB
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

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Canceled subscription {SubId} for customer {CustomerId}. Immediately={Immediately}",
            subscription.Id, customerId, request.CancelImmediately);

        // TODO: Send cancellation email

        var message = request.CancelImmediately
            ? "Subscription canceled immediately"
            : $"Subscription will be canceled at period end: {subscription.CurrentPeriodEnd:yyyy-MM-dd}";

        return new CancelSubscriptionResponse
        {
            SubscriptionId = subscription.Id,
            Status = subscription.Status.ToString(),
            CancelAt = subscription.CancelAt,
            CanceledAt = subscription.CanceledAt,
            CancelAtPeriodEnd = subscription.CancelAtPeriodEnd,
            Message = message
        };
    }

    public async Task<UpgradeSubscriptionResponse> UpgradeSubscriptionAsync(
        Guid customerId,
        UpgradeSubscriptionRequest request,
        CancellationToken ct = default)
    {
        var subscription = await _db.Subscriptions
            .Include(s => s.Plan)
            .FirstOrDefaultAsync(s => s.CustomerId == customerId && s.IsActive, ct);

        if (subscription is null)
            throw new BusinessException("No active subscription found for this customer");

        var newPlan = await _db.Plans
            .FirstOrDefaultAsync(p => p.Id == request.NewPlanId && p.IsActive, ct);

        if (newPlan is null)
            throw new BusinessException($"Plan not found or inactive: {request.NewPlanId}");

        if (subscription.PlanId == request.NewPlanId)
            throw new BusinessException("Customer is already on this plan");

        // Rule: Cannot downgrade during trial
        if (subscription.IsInTrial && newPlan.Amount < subscription.Plan.Amount)
            throw new BusinessException("Cannot downgrade plan during trial period");

        var updated = await _subscriptionService.ChangeSubscriptionPlanAsync(
            subscription.StripeSubscriptionId,
            newPlan.StripePriceId,
            ct);

        // Calculate proration amount (approximation)
        var prorationAmount = request.ApplyProration
            ? CalculateProration(subscription, newPlan)
            : 0m;

        var oldPlanId = subscription.PlanId;
        subscription.PlanId = request.NewPlanId;
        subscription.Status = MapSubscriptionStatus(updated.Status);

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Upgraded subscription {SubId} from plan {OldPlanId} to {NewPlanId}",
            subscription.Id, oldPlanId, request.NewPlanId);

        return new UpgradeSubscriptionResponse
        {
            SubscriptionId = subscription.Id,
            OldPlanId = oldPlanId,
            NewPlanId = request.NewPlanId,
            NewPlanName = newPlan.Name,
            ProrationAmount = prorationAmount,
            Status = subscription.Status.ToString(),
            EffectiveDate = DateTime.UtcNow,
            Message = $"Successfully upgraded to {newPlan.Name}"
        };
    }

    public async Task<SubscriptionDetailsResponse?> GetCustomerSubscriptionAsync(
        Guid customerId,
        CancellationToken ct = default)
    {
        var subscription = await _db.Subscriptions
            .Include(s => s.Plan)
            .FirstOrDefaultAsync(s => s.CustomerId == customerId && !s.IsDeleted, ct);

        if (subscription is null)
            return null;

        return await MapToDetailsResponse(subscription, ct);
    }


    private async Task<SubscriptionDetailsResponse> MapToDetailsResponse(
        Subscription subscription,
        CancellationToken ct)
    {
        // Ensure plan is loaded
        if (subscription.Plan is null)
            subscription.Plan = await _db.Plans.FindAsync([subscription.PlanId], ct)
                                ?? throw new BusinessException("Plan not found");

        var features = string.IsNullOrWhiteSpace(subscription.Plan.Features)
            ? null
            : JsonSerializer.Deserialize<List<string>>(subscription.Plan.Features);

        return new SubscriptionDetailsResponse
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
        };
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