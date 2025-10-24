using A2I.Application.StripeAbstraction.Webhooks;
using A2I.Core.Entities;
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
        var exists = await _db.WebhookEvents
            .AnyAsync(e => e.EventId == eventId, ct);
        
        if (exists)
        {
            _logger.LogInformation(
                "Event {EventId} already processed (idempotency check)",
                eventId);
        }
        
        return exists;
    }
    
    public async Task MarkProcessedAsync(string eventId, CancellationToken ct)
    {
        // Note: This is called BEFORE processing
        // Actual status will be updated after handler completes
        var webhookEvent = new WebhookEvent
        {
            EventId = eventId,
            EventType = "pending", // Will be updated by handler
            ProcessedAt = DateTime.UtcNow,
            Status = "processing",
            Id = IdGenHelper.NewGuidId()
        };
        
        _db.WebhookEvents.Add(webhookEvent);
        await _db.SaveChangesAsync(ct);
        
        _logger.LogDebug("Marked event {EventId} as processing", eventId);
    }
    
    public async Task UpdateEventStatusAsync(
        string eventId,
        string eventType,
        string status,
        string? errorMessage = null,
        CancellationToken ct = default)
    {
        var webhookEvent = await _db.WebhookEvents
            .FirstOrDefaultAsync(e => e.EventId == eventId, ct);
        
        if (webhookEvent != null)
        {
            webhookEvent.EventType = eventType;
            webhookEvent.Status = status;
            webhookEvent.ErrorMessage = errorMessage;
            webhookEvent.ProcessedAt = DateTime.UtcNow;
            
            if (status == "failed")
                webhookEvent.RetryCount++;
            
            await _db.SaveChangesAsync(ct);
        }
    }
}