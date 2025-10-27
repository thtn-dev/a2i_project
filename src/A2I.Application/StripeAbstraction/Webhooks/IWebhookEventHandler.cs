using Stripe;

namespace A2I.Application.StripeAbstraction.Webhooks;

public interface IWebhookEventHandler
{
    string EventType { get; }
    Task<WebhookHandlerResult> HandleAsync(Event stripeEvent, CancellationToken ct);
}

public record WebhookHandlerResult(
    bool Success,
    string Message,
    bool RequiresRetry = false,
    Dictionary<string, object>? Metadata = null
);