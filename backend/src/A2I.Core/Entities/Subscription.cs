using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using A2I.Core.Enums;
using BuildingBlocks.SharedKernel.Common;

namespace A2I.Core.Entities;

public class Subscription
    : EntityAggregateBase<Guid>, IAuditableEntity, ISoftDelete
{
    public Guid CustomerId { get; set; }
    public Guid PlanId { get; set; }

    [MaxLength(100)] public required string StripeSubscriptionId { get; set; }

    public SubscriptionStatus Status { get; set; }

    public DateTime CurrentPeriodStart { get; set; }
    public DateTime CurrentPeriodEnd { get; set; }
    public DateTime? CancelAt { get; set; }
    public DateTime? CanceledAt { get; set; }
    public bool CancelAtPeriodEnd { get; set; } = false;
    public DateTime? TrialStart { get; set; }
    public DateTime? TrialEnd { get; set; }
    public DateTime? EndedAt { get; set; }
    public int Quantity { get; set; } = 1;
    public string? Metadata { get; set; }

    // Navigations
    public Customer Customer { get; set; } = null!;
    public Plan Plan { get; set; } = null!;
    public ICollection<Invoice> Invoices { get; set; } = new List<Invoice>();

    // Computed
    [NotMapped]
    public bool IsActive =>
        Status is SubscriptionStatus.Active or SubscriptionStatus.Trialing;

    [NotMapped] public bool IsCanceled => Status == SubscriptionStatus.Canceled;

    [NotMapped]
    public int? DaysUntilRenewal =>
        CurrentPeriodEnd > DateTime.UtcNow
            ? (int)Math.Ceiling((CurrentPeriodEnd - DateTime.UtcNow).TotalDays)
            : null;

    [NotMapped]
    public bool IsInTrial =>
        TrialEnd.HasValue && TrialEnd.Value > DateTime.UtcNow;

    // Auditing + Soft delete
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}