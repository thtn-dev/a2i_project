namespace A2I.Application.StripeAbstraction.Subscriptions;

public enum ProrationMode
{
    /// <summary>Tạo các điều chỉnh prorate cho phần còn lại của kỳ hiện tại.</summary>
    CreateProrations = 0,
    /// <summary>Không tạo proration.</summary>
    None = 1
}

public enum PauseBehavior
{
    /// <summary>Hóa đơn mới sẽ ở trạng thái draft.</summary>
    KeepAsDraft = 0,
    /// <summary>Đánh dấu hóa đơn là uncollectible.</summary>
    MarkUncollectible = 1,
    /// <summary>Void hóa đơn.</summary>
    Void = 2
}

public sealed class CreateSubscriptionRequest
{
    public required string CustomerId { get; set; }         // cus_*
    public required string PriceId { get; set; }            // price_*
    public int Quantity { get; set; } = 1;

    // Trial
    public int? TrialPeriodDays { get; set; }               // hoặc
    public DateTimeOffset? TrialEnd { get; set; }           //  must be in the future (UTC)

    // Proration khi tạo (thường không cần, nhưng để sẵn cho đồng nhất)
    public ProrationMode Proration { get; set; } = ProrationMode.CreateProrations;

    // Metadata & tracking
    public Dictionary<string, string>? Metadata { get; set; }
    public string? PromotionCode { get; set; }              // nếu apply promo ngay lúc tạo
}

public sealed class UpdateSubscriptionRequest
{
    // Có thể cập nhật số lượng hoặc metadata chung
    public int? Quantity { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }

    // Proration behavior cho update
    public ProrationMode Proration { get; set; } = ProrationMode.CreateProrations;

    // Trial extend/shorten (Stripe yêu cầu thời điểm hợp lệ)
    public DateTimeOffset? TrialEnd { get; set; }
}

public sealed class SubscriptionView
{
    public required string Id { get; set; }                 // sub_*
    public required string CustomerId { get; set; }
    public required string PriceId { get; set; }
    public string? Status { get; set; }
    public DateTime? CurrentPeriodStart { get; set; }
    public DateTime? CurrentPeriodEnd { get; set; }
    public bool CancelAtPeriodEnd { get; set; }
    public DateTime? CancelAt { get; set; }
    public DateTime? CanceledAt { get; set; }
    public DateTime? TrialStart { get; set; }
    public DateTime? TrialEnd { get; set; }
    public long Quantity { get; set; }
    public IReadOnlyDictionary<string, string>? Metadata { get; set; }
    public string? LatestInvoiceId { get; set; }
}