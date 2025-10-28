using System.ComponentModel.DataAnnotations;
using A2I.Core.Enums;
using BuildingBlocks.SharedKernel.Common;

namespace A2I.Core.Entities;

public class StripeWebhookEvent : EntityAggregateBase<Guid>, IAuditableEntity
{
    [MaxLength(100)] public required string EventId { get; set; }

    [MaxLength(100)] public required string EventType { get; set; }

    public DateTime ProcessedAt { get; set; }

    [MaxLength(20)] public StripeWebhookStatus Status { get; set; } = StripeWebhookStatus.Queued;

    [MaxLength(2000)] public string? ErrorMessage { get; set; }

    public int RetryCount { get; set; } = 0;

    public string? RawData { get; set; }

    // Auditing
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}