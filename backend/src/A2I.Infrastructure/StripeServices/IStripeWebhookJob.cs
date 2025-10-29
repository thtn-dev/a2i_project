using A2I.Application.StripeAbstraction.Webhooks;
using A2I.Core.Enums;
using Hangfire;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Stripe;

namespace A2I.Infrastructure.StripeServices;

[AutomaticRetry(Attempts = 5, DelaysInSeconds = [60, 300, 900, 1800, 3600])]
public interface IStripeWebhookJob
{
    Task HandleAsync(string eventId, string eventType, CancellationToken ct);
}

public class StripeWebhookJob : IStripeWebhookJob
{
    private readonly IWebhookEventDispatcher _dispatcher;
    private readonly IEventIdempotencyStore _idempotencyStore;
    private readonly ILogger<StripeWebhookJob> _logger;

    public StripeWebhookJob(
        IWebhookEventDispatcher dispatcher,
        IEventIdempotencyStore idempotencyStore,
        ILogger<StripeWebhookJob> logger)
    {
        _dispatcher = dispatcher;
        _idempotencyStore = idempotencyStore;
        _logger = logger;
    }

    public async Task HandleAsync(string eventId, string eventType, CancellationToken ct)
    {
        var @event = await _idempotencyStore.GetEventStatusQueuedByEventIdAsync(eventId, ct);
        if (@event is null)
        {
            _logger.LogWarning("No queued event found for {EventId}", eventId);
            return;
        }

        var json = @event.RawData;
        if (string.IsNullOrWhiteSpace(json))
        {
            _logger.LogWarning("Missing JSON for {EventId}", eventId);
            throw new InvalidOperationException($"Missing JSON for {eventId}");
        }

        var stripeEvent = JsonConvert.DeserializeObject<Event>(json)
                          ?? throw new InvalidOperationException("Cannot deserialize Stripe event");

        _logger.LogInformation("Processing webhook {EventId} ({EventType})", eventId, eventType);

        var result = await _dispatcher.DispatchAsync(stripeEvent, ct);

        await _idempotencyStore.UpdateEventStatusAsync(
            eventId,
            eventType,
            result.Success ? StripeWebhookStatus.Processed : StripeWebhookStatus.Failed,
            result.Success ? null : result.Message,
            ct);

        if (!result.Success)
            throw new Exception(result.Message);

        _logger.LogInformation("Processed webhook {EventId} OK", eventId);
    }
}