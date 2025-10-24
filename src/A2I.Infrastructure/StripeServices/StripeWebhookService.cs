using A2I.Application.StripeAbstraction;
using A2I.Application.StripeAbstraction.Webhooks;
using Microsoft.Extensions.Logging;
using Stripe;

namespace A2I.Infrastructure.StripeServices;

public sealed class StripeWebhookService : IStripeWebhookService
{
    private readonly ILogger<StripeWebhookService> _logger;
    private readonly IEventIdempotencyStore? _store;

    public StripeWebhookService(ILogger<StripeWebhookService> logger, IEventIdempotencyStore? store = null)
    {
        _logger = logger;
        _store = store;
    }

    public Event ConstructEventAsync(string json, string signature, string secret)
    {
        try
        {
            // Signature validation PHẢI chạy đầu tiên
            var stripeEvent = EventUtility.ConstructEvent(json, signature, secret, throwOnApiVersionMismatch: false);
            _logger.LogInformation("Webhook verified. Event {EventId} type {Type}", stripeEvent.Id, stripeEvent.Type);
            return stripeEvent;
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(ex, "Invalid Stripe webhook signature");
            // service layer throw -> controller bắt và return 400
            throw StripeErrorMapper.Wrap(ex, "Invalid Stripe webhook signature.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to construct Stripe webhook event");
            throw new StripeServiceException("Failed to construct Stripe webhook event.", ex);
        }
    }

    public bool ValidateWebhookSignatureAsync(string json, string signature, string secret)
    {
        try
        {
            EventUtility.ValidateSignature(json, signature, secret);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> EnsureEventNotProcessedAsync(string eventId, CancellationToken ct = default)
    {
        if (_store is null)
        {
            // Không có store thì coi như luôn chưa xử lý (caller tự đảm bảo idempotency ở nơi khác)
            _logger.LogDebug("No idempotency store configured, skipping check for {EventId}", eventId);
            return false;
        }

        if (await _store.HasProcessedAsync(eventId, ct))
        {
            _logger.LogInformation("Webhook event already processed: {EventId}", eventId);
            return true;
        }

        await _store.MarkProcessedAsync(eventId, ct);
        _logger.LogDebug("Marked webhook event processed: {EventId}", eventId);
        return false;
    }
}