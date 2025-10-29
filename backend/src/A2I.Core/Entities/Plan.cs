using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using A2I.Core.Enums;
using BuildingBlocks.SharedKernel.Common;

namespace A2I.Core.Entities;

public class Plan
    : EntityAggregateBase<Guid>, IAuditableEntity, ISoftDelete
{
    [MaxLength(100)] public required string Name { get; set; }

    [MaxLength(500)] public string? Description { get; set; }

    [MaxLength(100)] public required string StripePriceId { get; set; }

    [MaxLength(100)] public required string StripeProductId { get; set; }

    public decimal Amount { get; set; } // numeric(18,2)

    [MaxLength(3)] public string Currency { get; set; } = "usd";

    public BillingInterval BillingInterval { get; set; }
    public int IntervalCount { get; set; } = 1;
    public int? TrialPeriodDays { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>JSON array (string), stored as JSONB.</summary>
    public string? Features { get; set; }

    public int SortOrder { get; set; } = 0;
    public string? Metadata { get; set; }

    // Navigations
    public ICollection<Subscription> Subscriptions { get; set; } = new List<Subscription>();

    // Computed
    [NotMapped] public string DisplayAmount => $"{Amount:0.##} {Currency}".ToLowerInvariant();

    [NotMapped]
    public string DisplayInterval =>
        BillingInterval switch
        {
            BillingInterval.Year => "per year",
            BillingInterval.Month => "per month",
            _ => "per interval"
        };

    // Auditing + Soft delete
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}

public static class PlanExtensions
{
    public static DateTime CalculateNextBillingDate(this Plan plan, DateTime fromDate)
    {
        return plan.BillingInterval switch
        {
            BillingInterval.Month => fromDate.AddDays(plan.IntervalCount),
            BillingInterval.Year => fromDate.AddDays(plan.IntervalCount),
            _ => fromDate
        };
    }
}