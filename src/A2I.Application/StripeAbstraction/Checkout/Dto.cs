namespace A2I.Application.StripeAbstraction.Checkout;

public sealed class CreateCheckoutRequest
{
    // Subscription mode
    public required string PriceId { get; set; } // price_*
    public int Quantity { get; set; } = 1;

    // Customer identity
    public string? CustomerId { get; set; } // cus_* (ưu tiên nếu có)
    public string? CustomerEmail { get; set; } // fallback nếu chưa có customer

    // URLs
    public required string SuccessUrl { get; set; } // có thể chứa query params (session_id etc.)
    public required string CancelUrl { get; set; }

    // UI / payments
    public List<string>? PaymentMethodTypes { get; set; } // e.g. ["card"]
    public bool AllowPromotionCodes { get; set; } = true;
    public string BillingAddressCollection { get; set; } = "auto"; // "auto" | "required"

    // Trial
    public int? TrialPeriodDays { get; set; } // optional trial qua subscription_data

    // Extra tracking
    public Dictionary<string, string>? Metadata { get; set; }
}

public sealed class CheckoutSessionView
{
    public required string Id { get; set; } // cs_*
    public string? Url { get; set; }
    public string? CustomerId { get; set; }
    public string? SubscriptionId { get; set; }
    public string? Status { get; set; }
    public string? PaymentStatus { get; set; }
    public DateTime? ExpiresAt { get; set; }
}