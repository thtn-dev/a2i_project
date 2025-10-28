using A2I.Application.StripeAbstraction.Webhooks;
using A2I.Core.Entities;
using A2I.Core.Enums;
using A2I.Infrastructure.Database;
using BuildingBlocks.Utils.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace A2I.Infrastructure.StripeServices;

public class DbEventIdempotencyStore : IEventIdempotencyStore
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<DbEventIdempotencyStore> _logger;

    public DbEventIdempotencyStore(
        ApplicationDbContext db,
        ILogger<DbEventIdempotencyStore> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<bool> HasProcessedAsync(string eventId, CancellationToken ct)
    {
        var exists = await _db.StripeWebhookEvents.AsNoTracking()
            .AnyAsync(e => e.EventId == eventId, ct);

        if (exists)
            _logger.LogInformation(
                "Event {EventId} already processed (idempotency check)",
                eventId);

        return exists;
    }

    public Task MarkQueuedAsync(string eventId, string eventType, string? json, CancellationToken ct)
    {
        var webhookEvent = new StripeWebhookEvent
        {
            EventId = eventId,
            EventType = eventType,
            ProcessedAt = DateTime.UtcNow,
            Status = StripeWebhookStatus.Queued,
            Id = IdGenHelper.NewGuidId(),
            RawData = json
        };
        _db.StripeWebhookEvents.Add(webhookEvent);
        return _db.SaveChangesAsync(ct);
    }

    public async Task UpdateEventStatusAsync(
        string eventId,
        string eventType,
        StripeWebhookStatus status,
        string? errorMessage = null,
        CancellationToken ct = default)
    {
        var webhookEvent = await _db.StripeWebhookEvents
            .FirstOrDefaultAsync(e => e.EventId == eventId, ct);

        if (webhookEvent != null)
        {
            webhookEvent.EventType = eventType;
            webhookEvent.Status = status;
            webhookEvent.ErrorMessage = errorMessage;
            webhookEvent.ProcessedAt = DateTime.UtcNow;

            if (status == StripeWebhookStatus.Failed)
                webhookEvent.RetryCount++;

            await _db.SaveChangesAsync(ct);
        }
    }

    public Task<StripeWebhookEvent?> GetEventStatusQueuedByEventIdAsync(string eventId, CancellationToken ct = default)
    {
        return _db.StripeWebhookEvents
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.EventId == eventId && e.Status == StripeWebhookStatus.Queued, ct);
    }
}