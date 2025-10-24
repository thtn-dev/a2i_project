namespace A2I.Application.StripeAbstraction.Checkout;

public interface IStripeCheckoutService
{
    Task<CheckoutSessionView> CreateCheckoutSessionAsync(CreateCheckoutRequest req, CancellationToken ct = default);
    Task<CheckoutSessionView?> GetCheckoutSessionAsync(string sessionId, CancellationToken ct = default);
    Task<bool> ExpireCheckoutSessionAsync(string sessionId, CancellationToken ct = default);
}