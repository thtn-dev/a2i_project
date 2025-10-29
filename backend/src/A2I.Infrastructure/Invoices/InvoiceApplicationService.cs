using A2I.Application.Common;
using A2I.Application.Invoices;
using A2I.Application.StripeAbstraction;
using A2I.Core.Enums;
using A2I.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace A2I.Infrastructure.Invoices;

public sealed class InvoiceApplicationService : IInvoiceApplicationService
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<InvoiceApplicationService> _logger;

    public InvoiceApplicationService(
        ApplicationDbContext db,
        ILogger<InvoiceApplicationService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<InvoiceListResponse> GetCustomerInvoicesAsync(
        Guid customerId,
        GetInvoicesRequest request,
        CancellationToken ct = default)
    {
        var customerExists = await _db.Customers
            .AnyAsync(c => c.Id == customerId, ct);

        if (!customerExists)
            throw new BusinessException($"Customer not found: {customerId}");

        var query = _db.Invoices
            .Include(i => i.Subscription)
            .ThenInclude(s => s!.Plan)
            .Where(i => i.CustomerId == customerId);

        if (!string.IsNullOrWhiteSpace(request.Status))
            if (Enum.TryParse<InvoiceStatus>(request.Status, true, out var status))
                query = query.Where(i => i.Status == status);

        if (request.FromDate.HasValue) query = query.Where(i => i.CreatedAt >= request.FromDate.Value);

        if (request.ToDate.HasValue)
        {
            var toDateEnd = request.ToDate.Value.Date.AddDays(1).AddTicks(-1);
            query = query.Where(i => i.CreatedAt <= toDateEnd);
        }

        var totalItems = await query.CountAsync(ct);

        var totalPages = (int)Math.Ceiling(totalItems / (double)request.PageSize);
        var skip = (request.Page - 1) * request.PageSize;

        var invoices = await query
            .OrderByDescending(i => i.CreatedAt)
            .Skip(skip)
            .Take(request.PageSize)
            .Select(i => new InvoiceItemDto
            {
                Id = i.Id,
                StripeInvoiceId = i.StripeInvoiceId,
                InvoiceNumber = i.InvoiceNumber,
                Status = i.Status.ToString(),
                Amount = i.Amount,
                AmountPaid = i.AmountPaid,
                AmountDue = i.AmountDue,
                Currency = i.Currency,
                PeriodStart = i.PeriodStart,
                PeriodEnd = i.PeriodEnd,
                DueDate = i.DueDate,
                PaidAt = i.PaidAt,
                CreatedAt = i.CreatedAt,
                IsPaid = i.IsPaid,
                IsOverdue = i.IsOverdue,
                HostedInvoiceUrl = i.HostedInvoiceUrl,
                InvoicePdf = i.InvoicePdf,
                SubscriptionId = i.SubscriptionId,
                PlanName = i.Subscription != null ? i.Subscription.Plan.Name : null
            })
            .ToListAsync(ct);

        _logger.LogInformation(
            "Retrieved {Count} invoices for customer {CustomerId} (Page {Page}/{TotalPages})",
            invoices.Count, customerId, request.Page, totalPages);

        return new InvoiceListResponse
        {
            Items = invoices,
            Pagination = new PaginationMetadata
            {
                CurrentPage = request.Page,
                PageSize = request.PageSize,
                TotalItems = totalItems,
                TotalPages = totalPages,
                HasPreviousPage = request.Page > 1,
                HasNextPage = request.Page < totalPages
            }
        };
    }


    public async Task<InvoiceDetailsResponse> GetInvoiceDetailsAsync(
        Guid customerId,
        Guid invoiceId,
        CancellationToken ct = default)
    {
        var invoice = await _db.Invoices
            .Include(i => i.Customer)
            .Include(i => i.Subscription)
            .ThenInclude(s => s!.Plan)
            .FirstOrDefaultAsync(i => i.Id == invoiceId, ct);

        if (invoice is null)
            throw new BusinessException($"Invoice not found: {invoiceId}");

        if (invoice.CustomerId != customerId)
        {
            _logger.LogWarning(
                "Customer {CustomerId} attempted to access invoice {InvoiceId} belonging to {OwnerId}",
                customerId, invoiceId, invoice.CustomerId);
            throw new BusinessException("You do not have permission to access this invoice");
        }

        var lineItems = new List<InvoiceLineItemDto>();

        if (invoice.Subscription?.Plan is not null)
        {
            var plan = invoice.Subscription.Plan;
            lineItems.Add(new InvoiceLineItemDto
            {
                Description = $"{plan.Name} - {plan.DisplayInterval}",
                Quantity = invoice.Subscription.Quantity,
                Amount = plan.Amount * invoice.Subscription.Quantity,
                Currency = plan.Currency
            });
        }

        var response = new InvoiceDetailsResponse
        {
            Id = invoice.Id,
            StripeInvoiceId = invoice.StripeInvoiceId,
            StripePaymentIntentId = invoice.StripePaymentIntentId,
            InvoiceNumber = invoice.InvoiceNumber,
            Status = invoice.Status.ToString(),
            Amount = invoice.Amount,
            AmountPaid = invoice.AmountPaid,
            AmountDue = invoice.AmountDue,
            Currency = invoice.Currency,
            PeriodStart = invoice.PeriodStart,
            PeriodEnd = invoice.PeriodEnd,
            DueDate = invoice.DueDate,
            PaidAt = invoice.PaidAt,
            CreatedAt = invoice.CreatedAt,
            AttemptCount = invoice.AttemptCount,
            LastAttemptAt = invoice.LastAttemptAt,
            NextAttemptAt = invoice.NextAttemptAt,
            IsPaid = invoice.IsPaid,
            IsOverdue = invoice.IsOverdue,
            HostedInvoiceUrl = invoice.HostedInvoiceUrl,
            InvoicePdf = invoice.InvoicePdf,
            CustomerId = invoice.CustomerId,
            CustomerEmail = invoice.Customer.Email,
            CustomerName = invoice.Customer.FullName,
            SubscriptionId = invoice.SubscriptionId,
            PlanName = invoice.Subscription?.Plan?.Name,
            PlanAmount = invoice.Subscription?.Plan?.Amount,
            LineItems = lineItems
        };

        _logger.LogInformation(
            "Retrieved invoice details {InvoiceId} for customer {CustomerId}",
            invoiceId, customerId);

        return response;
    }


    public async Task<InvoicePdfResponse> DownloadInvoicePdfAsync(
        Guid customerId,
        Guid invoiceId,
        CancellationToken ct = default)
    {
        var invoice = await _db.Invoices
            .FirstOrDefaultAsync(i => i.Id == invoiceId, ct);

        if (invoice is null)
            throw new BusinessException($"Invoice not found: {invoiceId}");

        if (invoice.CustomerId != customerId)
        {
            _logger.LogWarning(
                "Customer {CustomerId} attempted to download invoice {InvoiceId} belonging to {OwnerId}",
                customerId, invoiceId, invoice.CustomerId);
            throw new BusinessException("You do not have permission to access this invoice");
        }

        if (string.IsNullOrWhiteSpace(invoice.InvoicePdf))
        {
            _logger.LogWarning(
                "Invoice {InvoiceId} does not have a PDF URL",
                invoiceId);
            throw new BusinessException("Invoice PDF is not available");
        }

        _logger.LogInformation(
            "Retrieved PDF URL for invoice {InvoiceId} for customer {CustomerId}",
            invoiceId, customerId);

        return new InvoicePdfResponse
        {
            PdfUrl = invoice.InvoicePdf,
            InvoiceNumber = invoice.InvoiceNumber,
            ExpiresAt = null, // Stripe URLs don't expire (they're permanent)
            Message = "Invoice PDF URL retrieved successfully"
        };
    }
}