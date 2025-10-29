using Stripe;

namespace A2I.Application.StripeAbstraction.Webhooks;

public interface IWebhookEventDispatcher
{
    Task<WebhookHandlerResult> DispatchAsync(Event stripeEvent, CancellationToken ct);
}