namespace A2I.Application.Subscriptions;

/// <summary>
///     Orchestrates subscription business logic between DB and Stripe
/// </summary>
public interface ISubscriptionApplicationService
{
    /// <summary>
    ///     Start new subscription: create checkout session
    ///     Business Rules:
    ///     - One customer can only have one active subscription
    ///     - Must validate plan exists and is active
    /// </summary>
    Task<StartSubscriptionResponse> StartSubscriptionAsync(
        StartSubscriptionRequest request,
        CancellationToken ct = default);

    /// <summary>
    ///     Complete checkout: verify session and save subscription to DB
    ///     Business Rules:
    ///     - Verify checkout session is completed
    ///     - Create subscription record in DB
    ///     - Send welcome email (future)
    /// </summary>
    Task<SubscriptionDetailsResponse> CompleteCheckoutAsync(
        string checkoutSessionId,
        CancellationToken ct = default);

    /// <summary>
    ///     Cancel subscription
    ///     Business Rules:
    ///     - Validate customer ownership
    ///     - No refund policy
    ///     - Grace period: 7 days for failed payments
    /// </summary>
    Task<CancelSubscriptionResponse> CancelSubscriptionAsync(
        Guid customerId,
        CancelSubscriptionRequest request,
        CancellationToken ct = default);

    /// <summary>
    ///     Upgrade/change subscription plan
    ///     Business Rules:
    ///     - Cannot downgrade during trial period
    ///     - Apply proration by default
    ///     - Update both Stripe and DB
    /// </summary>
    Task<UpgradeSubscriptionResponse> UpgradeSubscriptionAsync(
        Guid customerId,
        UpgradeSubscriptionRequest request,
        CancellationToken ct = default);

    /// <summary>
    ///     Get customer's current subscription with plan details
    /// </summary>
    Task<SubscriptionDetailsResponse?> GetCustomerSubscriptionAsync(
        Guid customerId,
        CancellationToken ct = default);
}