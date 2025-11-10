using A2I.Application.Common;
using A2I.Application.Invoices;
using A2I.WebAPI.Extensions;

namespace A2I.WebAPI.Endpoints.Invoices;

/// <summary>
///     Invoice management endpoints
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
            .WithStandardResponses<InvoiceDetailsResponse>();

        group.MapGet("/{customerId:guid}/{invoiceId:guid}/pdf", DownloadInvoicePdf)
            .WithName("DownloadInvoicePdf")
            .WithApiMetadata(
                "Download invoice PDF",
                "Gets a download URL for the invoice PDF. URL is hosted by Stripe and does not expire.")
            .WithStandardResponses<InvoicePdfResponse>();
            
        return group;
    }


    private static async Task<IResult> GetCustomerInvoices(
        Guid customerId,
        [AsParameters] GetInvoicesRequest queryParams,
        IInvoiceApplicationService invoiceService,
        CancellationToken ct)
    {
        if (!EndpointExtensions.ValidatePagination(
                queryParams.Page,
                queryParams.PageSize,
                out var validationError))
            return validationError!;

        if (queryParams is { FromDate: not null, ToDate: not null } &&
            queryParams.FromDate > queryParams.ToDate)
            return Results.BadRequest("FromDate cannot be greater than ToDate");

        var request = new GetInvoicesRequest
        {
            Page = queryParams.Page,
            PageSize = queryParams.PageSize,
            Status = queryParams.Status,
            FromDate = queryParams.FromDate,
            ToDate = queryParams.ToDate
        };
        var result = await invoiceService.GetCustomerInvoicesAsync(
            customerId, request, ct);

        return result.ToHttpResult();
    }

    private static async Task<IResult> GetInvoiceDetails(
        Guid customerId,
        Guid invoiceId,
        IInvoiceApplicationService invoiceService,
        CancellationToken ct)
    {
        var result = await invoiceService.GetInvoiceDetailsAsync(
            customerId, invoiceId, ct);
        return result.ToHttpResult();
    }
    
    private static async Task<IResult> DownloadInvoicePdf(
        Guid customerId,
        Guid invoiceId,
        IInvoiceApplicationService invoiceService,
        CancellationToken ct)
    {
        var result = await invoiceService.DownloadInvoicePdfAsync(
            customerId, invoiceId, ct);
        return result.ToHttpResult();
    }
}