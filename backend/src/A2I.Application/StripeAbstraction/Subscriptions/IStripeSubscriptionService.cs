namespace A2I.Application.StripeAbstraction.Subscriptions;

public interface IStripeSubscriptionService
{
    Task<SubscriptionView> CreateSubscriptionAsync(CreateSubscriptionRequest req, CancellationToken ct = default);
    Task<SubscriptionView?> GetSubscriptionAsync(string subscriptionId, CancellationToken ct = default);

    Task<SubscriptionView> UpdateSubscriptionAsync(string subscriptionId, UpdateSubscriptionRequest req,
        CancellationToken ct = default);

    Task<SubscriptionView> CancelSubscriptionAsync(string subscriptionId, bool immediately,
        CancellationToken ct = default);

    Task<SubscriptionView> ReactivateSubscriptionAsync(string subscriptionId, CancellationToken ct = default);

    Task<SubscriptionView> ChangeSubscriptionPlanAsync(string subscriptionId, string newPriceId,
        CancellationToken ct = default);

    Task<SubscriptionView> PauseSubscriptionAsync(string subscriptionId,
        PauseBehavior behavior = PauseBehavior.KeepAsDraft, CancellationToken ct = default);

    Task<SubscriptionView> ResumeSubscriptionAsync(string subscriptionId, CancellationToken ct = default);
}