using A2I.Application.Common;
using A2I.Application.Invoices;
using A2I.WebAPI.Extensions;

namespace A2I.WebAPI.Endpoints.Invoices;

/// <summary>
/// Invoice management endpoints
/// </summary>
public static class InvoiceEndpoints
{
    public static RouteGroupBuilder MapInvoiceEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/{customerId:guid}", GetCustomerInvoices)
            .WithName("GetCustomerInvoices")
            .WithApiMetadata(
                "Get customer invoices",
                "Retrieves paginated list of invoices for a customer with optional filtering by status and date range.")
            .WithPaginatedResponses<InvoiceItemDto>();

        group.MapGet("/{customerId:guid}/{invoiceId:guid}", GetInvoiceDetails)
            .WithName("GetInvoiceDetails")
            .WithApiMetadata(
                "Get invoice details",
                "Retrieves detailed information about a specific invoice including line items and payment attempts.")
            .Produces<ApiResponse<InvoiceDetailsResponse>>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError);

        group.MapGet("/{customerId:guid}/{invoiceId:guid}/pdf", DownloadInvoicePdf)
            .WithName("DownloadInvoicePdf")
            .WithApiMetadata(
                "Download invoice PDF",
                "Gets a download URL for the invoice PDF. URL is hosted by Stripe and does not expire.")
            .Produces<ApiResponse<InvoicePdfResponse>>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError);

        return group;
    }

    // ==================== 1. GET CUSTOMER INVOICES (PAGINATED) ====================

    private static async Task<IResult> GetCustomerInvoices(
        Guid customerId,
        [AsParameters] GetInvoicesQueryParams queryParams,
        IInvoiceApplicationService invoiceService,
        CancellationToken ct)
    {
        // Validate pagination
        if (!EndpointExtensions.ValidatePagination(
            queryParams.Page, 
            queryParams.PageSize, 
            out var validationError))
        {
            return validationError!;
        }

        // Validate date range
        if (queryParams is { FromDate: not null, ToDate: not null } && 
            queryParams.FromDate > queryParams.ToDate)
        {
            return EndpointExtensions.BadRequest(
                ErrorCodes.VALIDATION_RANGE,
                "FromDate cannot be greater than ToDate");
        }

        var request = new GetInvoicesRequest
        {
            Page = queryParams.Page,
            PageSize = queryParams.PageSize,
            Status = queryParams.Status,
            FromDate = queryParams.FromDate,
            ToDate = queryParams.ToDate
        };

        return await EndpointExtensions.ExecutePaginatedAsync(
            async () =>
            {
                var response = await invoiceService.GetCustomerInvoicesAsync(
                    customerId, request, ct);
                return (response.Items, response.Pagination);
            });
    }

    // ==================== 2. GET INVOICE DETAILS ====================

    private static async Task<IResult> GetInvoiceDetails(
        Guid customerId,
        Guid invoiceId,
        IInvoiceApplicationService invoiceService,
        CancellationToken ct)
    {
        return await EndpointExtensions.ExecuteAsync(
            async () => await invoiceService.GetInvoiceDetailsAsync(
                customerId, invoiceId, ct),
            "Invoice details retrieved successfully");
    }

    // ==================== 3. DOWNLOAD INVOICE PDF ====================

    private static async Task<IResult> DownloadInvoicePdf(
        Guid customerId,
        Guid invoiceId,
        IInvoiceApplicationService invoiceService,
        CancellationToken ct)
    {
        return await EndpointExtensions.ExecuteAsync(
            async () => await invoiceService.DownloadInvoicePdfAsync(
                customerId, invoiceId, ct),
            "Invoice PDF URL retrieved successfully");
    }
}

// ==================== QUERY PARAMETERS ====================

/// <summary>
/// Query parameters for invoice listing
/// </summary>
public sealed class GetInvoicesQueryParams
{
    /// <summary>
    /// Page number (1-based)
    /// </summary>
    public int Page { get; set; } = 1;

    /// <summary>
    /// Items per page (max 100)
    /// </summary>
    public int PageSize { get; set; } = 10;

    /// <summary>
    /// Filter by invoice status (Draft, Open, Paid, Uncollectible, Void)
    /// </summary>
    public string? Status { get; set; }

    /// <summary>
    /// Filter invoices created from this date
    /// </summary>
    public DateTime? FromDate { get; set; }

    /// <summary>
    /// Filter invoices created until this date
    /// </summary>
    public DateTime? ToDate { get; set; }
}