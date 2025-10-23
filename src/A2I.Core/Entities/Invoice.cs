using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using A2I.Core.Enums;
using BuildingBlocks.SharedKernel.Common;

namespace A2I.Core.Entities;

public class Invoice
    : EntityAggregateBase<Guid>, IAuditableEntity, ISoftDelete
{
    public Guid CustomerId { get; set; }
    public Guid? SubscriptionId { get; set; }

    [MaxLength(100)] public required string StripeInvoiceId { get; set; }

    [MaxLength(100)] public string? StripePaymentIntentId { get; set; }

    [MaxLength(50)] public string? InvoiceNumber { get; set; }

    public InvoiceStatus Status { get; set; }

    public decimal Amount { get; set; } // numeric(18,2)
    public decimal AmountPaid { get; set; } // numeric(18,2)
    public decimal AmountDue { get; set; } // numeric(18,2)

    [MaxLength(3)] public string Currency { get; set; } = "usd";

    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public DateTime? DueDate { get; set; }
    public DateTime? PaidAt { get; set; }
    public int AttemptCount { get; set; } = 0;
    public DateTime? LastAttemptAt { get; set; }
    public DateTime? NextAttemptAt { get; set; }

    [MaxLength(500)] public string? HostedInvoiceUrl { get; set; }

    [MaxLength(500)] public string? InvoicePdf { get; set; }

    public string? Metadata { get; set; }

    // Navigations
    public Customer Customer { get; set; } = null!;
    public Subscription? Subscription { get; set; }

    // Computed
    [NotMapped] public bool IsPaid => Status == InvoiceStatus.Paid;
    [NotMapped] public bool IsOverdue => Status == InvoiceStatus.Open && DueDate < DateTime.UtcNow;

    // Auditing + Soft delete
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
}