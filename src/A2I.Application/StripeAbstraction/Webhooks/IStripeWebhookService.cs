using A2I.Core.Entities;
using Stripe;

namespace A2I.Application.StripeAbstraction.Webhooks;

public interface IStripeWebhookService
{
    /// <summary>
    ///     Validate & construct event (signature validation MUST be first).
    ///     Throw StripeServiceException nếu invalid.
    /// </summary>
    Event ConstructEvent(string json, string signature, string secret);
}

/// <summary>
///     Abstraction store để lưu dấu đã xử lý event (idempotency).
/// </summary>
public interface IEventIdempotencyStore
{
    Task<bool> HasProcessedAsync(string eventId, CancellationToken ct);
    Task MarkProcessedAsync(string eventId, CancellationToken ct);
    Task MarkQueuedAsync(string eventId, string eventType, string? json, CancellationToken ct);

    Task UpdateEventStatusAsync(string eventId, string eventType, string status,
        string? errorMessage = null, CancellationToken ct = default);

    Task<WebhookEvent?> GetEventStatusQueuedByEventIdAsync(string eventId, CancellationToken ct = default);
}