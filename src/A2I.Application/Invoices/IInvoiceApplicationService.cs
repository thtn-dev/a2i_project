namespace A2I.Application.Invoices;

/// <summary>
/// Orchestrates invoice management and retrieval
/// </summary>
public interface IInvoiceApplicationService
{
    /// <summary>
    /// Get customer invoices with pagination and filtering
    /// Returns:
    /// - Paginated list of invoices
    /// - Can filter by status, date range
    /// - Ordered by created date (newest first)
    /// </summary>
    Task<InvoiceListResponse> GetCustomerInvoicesAsync(
        Guid customerId,
        GetInvoicesRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Get invoice details with line items
    /// Business Rules:
    /// - Verify customer ownership
    /// - Include subscription details if applicable
    /// - Show payment attempts and status
    /// </summary>
    Task<InvoiceDetailsResponse> GetInvoiceDetailsAsync(
        Guid customerId,
        Guid invoiceId,
        CancellationToken ct = default);

    /// <summary>
    /// Get invoice PDF download URL
    /// Business Rules:
    /// - Verify customer ownership
    /// - Return Stripe hosted invoice PDF URL
    /// - URL is temporary (expires after some time)
    /// </summary>
    Task<InvoicePdfResponse> DownloadInvoicePdfAsync(
        Guid customerId,
        Guid invoiceId,
        CancellationToken ct = default);
}