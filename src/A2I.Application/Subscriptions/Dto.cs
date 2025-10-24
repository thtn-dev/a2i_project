using System.ComponentModel.DataAnnotations;

namespace A2I.Application.Subscriptions;

// ==================== REQUESTS ====================

public sealed class StartSubscriptionRequest
{
    [Required]
    public Guid CustomerId { get; set; }

    [Required]
    public Guid PlanId { get; set; }

    [Required]
    [Url]
    public required string SuccessUrl { get; set; }

    [Required]
    [Url]
    public required string CancelUrl { get; set; }

    public bool AllowPromotionCodes { get; set; } = true;

    public Dictionary<string, string>? Metadata { get; set; }
}

public sealed class CancelSubscriptionRequest
{
    /// <summary>
    /// true = cancel immediately (end now)
    /// false = cancel at period end (default)
    /// </summary>
    public bool CancelImmediately { get; set; } = false;

    public string? Reason { get; set; }
}

public sealed class UpgradeSubscriptionRequest
{
    [Required]
    public Guid NewPlanId { get; set; }

    /// <summary>
    /// Apply proration or not (default: true)
    /// </summary>
    public bool ApplyProration { get; set; } = true;
}

// ==================== RESPONSES ====================

public sealed class StartSubscriptionResponse
{
    public required string CheckoutSessionId { get; set; }
    public required string CheckoutUrl { get; set; }
    public DateTime ExpiresAt { get; set; }
}

public sealed class SubscriptionDetailsResponse
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public Guid PlanId { get; set; }
    
    public string StripeSubscriptionId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    
    public DateTime CurrentPeriodStart { get; set; }
    public DateTime CurrentPeriodEnd { get; set; }
    public DateTime? CancelAt { get; set; }
    public DateTime? CanceledAt { get; set; }
    public bool CancelAtPeriodEnd { get; set; }
    
    public DateTime? TrialStart { get; set; }
    public DateTime? TrialEnd { get; set; }
    public bool IsInTrial { get; set; }
    
    public int Quantity { get; set; }
    public int? DaysUntilRenewal { get; set; }
    
    // Plan details
    public PlanDetailsDto? Plan { get; set; }
}

public sealed class PlanDetailsDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "usd";
    public string BillingInterval { get; set; } = string.Empty;
    public int? TrialPeriodDays { get; set; }
    public List<string>? Features { get; set; }
}

public sealed class CancelSubscriptionResponse
{
    public Guid SubscriptionId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime? CancelAt { get; set; }
    public DateTime? CanceledAt { get; set; }
    public bool CancelAtPeriodEnd { get; set; }
    public string Message { get; set; } = string.Empty;
}

public sealed class UpgradeSubscriptionResponse
{
    public Guid SubscriptionId { get; set; }
    public Guid OldPlanId { get; set; }
    public Guid NewPlanId { get; set; }
    public string NewPlanName { get; set; } = string.Empty;
    public decimal ProrationAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime EffectiveDate { get; set; }
    public string Message { get; set; } = string.Empty;
}