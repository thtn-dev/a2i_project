namespace A2I.Application.StripeAbstraction.Webhooks;

public interface IStripeWebhookService
{
    /// <summary>
    /// Validate & construct event (signature validation MUST be first).
    /// Throw StripeServiceException nếu invalid.
    /// </summary>
    Stripe.Event ConstructEventAsync(string json, string signature, string secret);

    /// <summary>
    /// Validate signature only (true/false). Không throw.
    /// </summary>
    bool ValidateWebhookSignatureAsync(string json, string signature, string secret);

    /// <summary>
    /// Idempotency check (optional): return true nếu EventId đã xử lý; nếu false, mark processed.
    /// Tùy dự án: có thể cấy Redis/DB. Ở đây để interface chung.
    /// </summary>
    Task<bool> EnsureEventNotProcessedAsync(string eventId, CancellationToken ct = default);
}

/// <summary>
/// Abstraction store để lưu dấu đã xử lý event (idempotency).
/// </summary>
public interface IEventIdempotencyStore
{
    Task<bool> HasProcessedAsync(string eventId, CancellationToken ct);
    Task MarkProcessedAsync(string eventId, CancellationToken ct);
    Task UpdateEventStatusAsync(string eventId, string eventType, string status, 
        string? errorMessage = null, CancellationToken ct = default);
}