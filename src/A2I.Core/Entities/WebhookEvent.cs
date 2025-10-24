using System.ComponentModel.DataAnnotations;
using BuildingBlocks.SharedKernel.Common;

namespace A2I.Core.Entities;

public class WebhookEvent : EntityAggregateBase<Guid>, IAuditableEntity
{
    [MaxLength(100)]
    public required string EventId { get; set; }  // Stripe event_xxx
    
    [MaxLength(100)]
    public required string EventType { get; set; }  // e.g., "invoice.paid"
    
    public DateTime ProcessedAt { get; set; }
    
    [MaxLength(20)]
    public string Status { get; set; } = "processed"; // processed | failed | retrying
    
    [MaxLength(2000)]
    public string? ErrorMessage { get; set; }
    
    public int RetryCount { get; set; } = 0;
    
    public string? RawData { get; set; }  // Store raw JSON for debugging
    
    // Auditing
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}
