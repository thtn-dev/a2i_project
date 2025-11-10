namespace A2I.Application.Customers;

// ==================== REQUESTS ====================

public record CreateOrUpdateCustomerRequest
{
    public Guid CustomerId { get; set; }

    public required string Email { get; set; }

    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    public string? Phone { get; set; }

    public string? CompanyName { get; set; }

    public string? PaymentMethodId { get; set; }

    public Dictionary<string, string>? Metadata { get; set; }
}

public record UpdatePaymentMethodRequest
{
    public required string PaymentMethodId { get; set; }

    /// <summary>
    ///     Set as default payment method for future invoices
    /// </summary>
    public bool SetAsDefault { get; set; } = true;
}

public record GetPortalUrlRequest
{
    /// <summary>
    ///     URL to redirect customer after they finish managing their subscription
    /// </summary>
    public string ReturnUrl { get; set; } = string.Empty;
}

// ==================== RESPONSES ====================

public sealed class CustomerDetailsResponse
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? StripeCustomerId { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? CompanyName { get; set; }
    public string Currency { get; set; } = "usd";
    public bool HasActiveSubscription { get; set; }

    // Active subscription (if any)
    public SubscriptionSummaryDto? ActiveSubscription { get; set; }

    // Recent invoices (last 5)
    public List<InvoiceSummaryDto> RecentInvoices { get; set; } = [];

    // Payment methods
    public List<PaymentMethodDto> PaymentMethods { get; set; } = [];
    public string? DefaultPaymentMethodId { get; set; }
}

public sealed class SubscriptionSummaryDto
{
    public Guid Id { get; set; }
    public string Status { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "usd";
    public DateTime CurrentPeriodEnd { get; set; }
    public int? DaysUntilRenewal { get; set; }
    public bool IsInTrial { get; set; }
    public DateTime? TrialEnd { get; set; }
}

public sealed class InvoiceSummaryDto
{
    public Guid Id { get; set; }
    public string StripeInvoiceId { get; set; } = string.Empty;
    public string? InvoiceNumber { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal AmountPaid { get; set; }
    public decimal AmountDue { get; set; }
    public string Currency { get; set; } = "usd";
    public DateTime? DueDate { get; set; }
    public DateTime? PaidAt { get; set; }
    public bool IsPaid { get; set; }
    public bool IsOverdue { get; set; }
    public string? HostedInvoiceUrl { get; set; }
}

public sealed class PaymentMethodDto
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = "card";
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public long? ExpMonth { get; set; }
    public long? ExpYear { get; set; }
    public bool IsDefault { get; set; }
}

public sealed class UpdatePaymentMethodResponse
{
    public string CustomerId { get; set; } = string.Empty;
    public string PaymentMethodId { get; set; } = string.Empty;
    public bool SetAsDefault { get; set; }
    public string Message { get; set; } = string.Empty;
}

public sealed class CustomerPortalResponse
{
    public required string PortalUrl { get; set; }
    public string PortalSessionId { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}