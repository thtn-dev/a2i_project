using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using A2I.Core.Enums;
using BuildingBlocks.SharedKernel.Common;

namespace A2I.Core.Entities;

public class Customer
    : EntityAggregateBase<Guid>, IAuditableEntity, ISoftDelete
{
    [MaxLength(255)] public required string Email { get; set; }

    [MaxLength(100)] public string? StripeCustomerId { get; set; }

    [MaxLength(100)] public string? FirstName { get; set; }

    [MaxLength(100)] public string? LastName { get; set; }

    [MaxLength(20)] public string? Phone { get; set; }

    [MaxLength(200)] public string? CompanyName { get; set; }

    [MaxLength(50)] public string? TaxId { get; set; }

    [MaxLength(3)] public string Currency { get; set; } = "usd";

    /// <summary>Free-form extra data. Stored as JSONB.</summary>
    public string? Metadata { get; set; }

    /// <summary>Foreign key to Identity User (string id)</summary>
    [MaxLength(256)]
    public required string UserId { get; set; }

    // Navigations
    public ICollection<Subscription> Subscriptions { get; set; } = new List<Subscription>();
    public ICollection<Invoice> Invoices { get; set; } = new List<Invoice>();

    // Computed
    [NotMapped] public string FullName => $"{FirstName} {LastName}".Trim();

    [NotMapped]
    public bool HasActiveSubscription =>
        Subscriptions?.Any(s => s.Status is SubscriptionStatus.Active or SubscriptionStatus.Trialing) == true;

    // Auditing + Soft delete
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
}