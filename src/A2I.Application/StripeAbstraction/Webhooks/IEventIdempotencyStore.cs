using A2I.Core.Entities;
using A2I.Core.Enums;

namespace A2I.Application.StripeAbstraction.Webhooks;

/// <summary>
///     Abstraction store event (idempotency).
/// </summary>
public interface IEventIdempotencyStore
{
    Task<bool> HasProcessedAsync(string eventId, CancellationToken ct);
    Task MarkQueuedAsync(string eventId, string eventType, string? json, CancellationToken ct);
    Task UpdateEventStatusAsync(string eventId, string eventType, StripeWebhookStatus status,
        string? errorMessage = null, CancellationToken ct = default);
    Task<StripeWebhookEvent?> GetEventStatusQueuedByEventIdAsync(string eventId, CancellationToken ct = default);
}