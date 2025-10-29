using A2I.Application.StripeAbstraction.Webhooks;
using Microsoft.Extensions.Logging;
using Stripe;

namespace A2I.Infrastructure.StripeServices;

public class WebhookEventDispatcher : IWebhookEventDispatcher
{
    private readonly IEnumerable<IWebhookEventHandler> _handlers;
    private readonly ILogger<WebhookEventDispatcher> _logger;

    public WebhookEventDispatcher(
        IEnumerable<IWebhookEventHandler> handlers,
        ILogger<WebhookEventDispatcher> logger)
    {
        _handlers = handlers;
        _logger = logger;
    }

    public async Task<WebhookHandlerResult> DispatchAsync(Event stripeEvent, CancellationToken ct = default)
    {
        var handler = _handlers.FirstOrDefault(h => h.EventType == stripeEvent.Type);

        if (handler == null)
        {
            _logger.LogWarning(
                "No handler registered for event type: {EventType} (Event ID: {EventId})",
                stripeEvent.Type, stripeEvent.Id);

            return new WebhookHandlerResult(
                true,
                $"No handler for {stripeEvent.Type} (ignored)"
            );
        }

        _logger.LogInformation(
            "Dispatching {EventType} to {HandlerType}",
            stripeEvent.Type, handler.GetType().Name);

        return await handler.HandleAsync(stripeEvent, ct);
    }
}