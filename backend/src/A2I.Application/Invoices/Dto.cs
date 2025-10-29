using System.ComponentModel.DataAnnotations;
using A2I.Application.Common;

namespace A2I.Application.Invoices;

// ==================== REQUESTS ====================

public sealed class GetInvoicesRequest
{
    /// <summary>
    ///     Page number (1-based)
    /// </summary>
    [Range(1, int.MaxValue)]
    public int Page { get; set; } = 1;

    /// <summary>
    ///     Items per page
    /// </summary>
    [Range(1, 100)]
    public int PageSize { get; set; } = 10;

    /// <summary>
    ///     Filter by status (optional)
    /// </summary>
    public string? Status { get; set; }

    /// <summary>
    ///     Filter by date range - start (optional)
    /// </summary>
    public DateTime? FromDate { get; set; }

    /// <summary>
    ///     Filter by date range - end (optional)
    /// </summary>
    public DateTime? ToDate { get; set; }
}

// ==================== RESPONSES ====================

public sealed class InvoiceListResponse
{
    public List<InvoiceItemDto> Items { get; set; } = new();
    public PaginationMetadata Pagination { get; set; } = new();
}

public sealed class InvoiceItemDto
{
    public Guid Id { get; set; }
    public string StripeInvoiceId { get; set; } = string.Empty;
    public string? InvoiceNumber { get; set; }
    public string Status { get; set; } = string.Empty;

    public decimal Amount { get; set; }
    public decimal AmountPaid { get; set; }
    public decimal AmountDue { get; set; }
    public string Currency { get; set; } = "usd";

    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public DateTime? DueDate { get; set; }
    public DateTime? PaidAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public bool IsPaid { get; set; }
    public bool IsOverdue { get; set; }

    public string? HostedInvoiceUrl { get; set; }
    public string? InvoicePdf { get; set; }

    // Subscription info (if applicable)
    public Guid? SubscriptionId { get; set; }
    public string? PlanName { get; set; }
}

public sealed class InvoiceDetailsResponse
{
    public Guid Id { get; set; }
    public string StripeInvoiceId { get; set; } = string.Empty;
    public string? StripePaymentIntentId { get; set; }
    public string? InvoiceNumber { get; set; }
    public string Status { get; set; } = string.Empty;

    public decimal Amount { get; set; }
    public decimal AmountPaid { get; set; }
    public decimal AmountDue { get; set; }
    public string Currency { get; set; } = "usd";

    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public DateTime? DueDate { get; set; }
    public DateTime? PaidAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public long AttemptCount { get; set; }
    public DateTime? LastAttemptAt { get; set; }
    public DateTime? NextAttemptAt { get; set; }

    public bool IsPaid { get; set; }
    public bool IsOverdue { get; set; }

    public string? HostedInvoiceUrl { get; set; }
    public string? InvoicePdf { get; set; }

    // Customer info
    public Guid CustomerId { get; set; }
    public string CustomerEmail { get; set; } = string.Empty;
    public string? CustomerName { get; set; }

    // Subscription info (if applicable)
    public Guid? SubscriptionId { get; set; }
    public string? PlanName { get; set; }
    public decimal? PlanAmount { get; set; }

    // Line items (can be expanded in future)
    public List<InvoiceLineItemDto> LineItems { get; set; } = new();
}

public sealed class InvoiceLineItemDto
{
    public string Description { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "usd";
}

public sealed class InvoicePdfResponse
{
    public required string PdfUrl { get; set; }
    public string? InvoiceNumber { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string Message { get; set; } = string.Empty;
}