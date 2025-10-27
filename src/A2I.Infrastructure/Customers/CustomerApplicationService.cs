using A2I.Application.Customers;
using A2I.Application.StripeAbstraction;
using A2I.Application.StripeAbstraction.Customers;
using A2I.Application.StripeAbstraction.Portal;
using A2I.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace A2I.Infrastructure.Customers;

public sealed class CustomerApplicationService : ICustomerApplicationService
{
    private readonly IStripeCustomerService _customerService;
    private readonly ApplicationDbContext _db;
    private readonly ILogger<CustomerApplicationService> _logger;
    private readonly IStripePortalService _portalService;

    public CustomerApplicationService(
        ApplicationDbContext db,
        IStripeCustomerService customerService,
        IStripePortalService portalService,
        ILogger<CustomerApplicationService> logger)
    {
        _db = db;
        _customerService = customerService;
        _portalService = portalService;
        _logger = logger;
    }

    // ==================== CREATE OR UPDATE CUSTOMER ====================

    public async Task<CustomerDetailsResponse> CreateOrUpdateCustomerAsync(
        CreateOrUpdateCustomerRequest request,
        CancellationToken ct = default)
    {
        // 1. Get customer from DB
        var customer = await _db.Customers
            .FirstOrDefaultAsync(c => c.Id == request.CustomerId, ct);

        if (customer is null)
            throw new BusinessException($"Customer not found: {request.CustomerId}");

        // 2. Create or update Stripe customer
        if (string.IsNullOrWhiteSpace(customer.StripeCustomerId))
        {
            // Create new Stripe customer
            var createRequest = new CreateCustomerRequest
            {
                Email = request.Email,
                Name = $"{request.FirstName} {request.LastName}".Trim(),
                Phone = request.Phone,
                Description = request.CompanyName,
                PaymentMethodId = request.PaymentMethodId,
                Metadata = request.Metadata ?? new Dictionary<string, string>()
            };

            // Add internal customer ID to metadata
            createRequest.Metadata["internal_customer_id"] = customer.Id.ToString();

            var stripeCustomer = await _customerService.CreateCustomerAsync(createRequest, ct);

            // Update DB with Stripe customer ID
            customer.StripeCustomerId = stripeCustomer.Id;

            _logger.LogInformation(
                "Created Stripe customer {StripeCustomerId} for customer {CustomerId}",
                stripeCustomer.Id, customer.Id);
        }
        else
        {
            // Update existing Stripe customer
            var updateRequest = new UpdateCustomerRequest
            {
                Email = request.Email,
                Name = $"{request.FirstName} {request.LastName}".Trim(),
                Phone = request.Phone,
                Description = request.CompanyName,
                DefaultPaymentMethodId = request.PaymentMethodId,
                Metadata = request.Metadata
            };

            await _customerService.UpdateCustomerAsync(customer.StripeCustomerId, updateRequest, ct);

            _logger.LogInformation(
                "Updated Stripe customer {StripeCustomerId} for customer {CustomerId}",
                customer.StripeCustomerId, customer.Id);
        }

        // 3. Update customer in DB
        customer.Email = request.Email;
        customer.FirstName = request.FirstName;
        customer.LastName = request.LastName;
        customer.Phone = request.Phone;
        customer.CompanyName = request.CompanyName;

        await _db.SaveChangesAsync(ct);

        // 4. Return customer details
        return await GetCustomerDetailsAsync(customer.Id, ct);
    }

    // ==================== GET CUSTOMER DETAILS ====================

    public async Task<CustomerDetailsResponse> GetCustomerDetailsAsync(
        Guid customerId,
        CancellationToken ct = default)
    {
        // 1. Get customer with related data
        var customer = await _db.Customers
            .Include(c => c.Subscriptions.Where(s => !s.IsDeleted))
            .ThenInclude(s => s.Plan)
            .Include(c => c.Invoices.Where(i => !i.IsDeleted))
            .FirstOrDefaultAsync(c => c.Id == customerId, ct);

        if (customer is null)
            throw new BusinessException($"Customer not found: {customerId}");

        // 2. Get active subscription
        var activeSubscription = customer.Subscriptions
            .FirstOrDefault(s => s.IsActive);

        SubscriptionSummaryDto? subscriptionSummary = null;
        if (activeSubscription is not null)
            subscriptionSummary = new SubscriptionSummaryDto
            {
                Id = activeSubscription.Id,
                Status = activeSubscription.Status.ToString(),
                PlanName = activeSubscription.Plan.Name,
                Amount = activeSubscription.Plan.Amount,
                Currency = activeSubscription.Plan.Currency,
                CurrentPeriodEnd = activeSubscription.CurrentPeriodEnd,
                DaysUntilRenewal = activeSubscription.DaysUntilRenewal,
                IsInTrial = activeSubscription.IsInTrial,
                TrialEnd = activeSubscription.TrialEnd
            };

        // 3. Get recent invoices (last 5)
        var recentInvoices = customer.Invoices
            .OrderByDescending(i => i.CreatedAt)
            .Take(5)
            .Select(i => new InvoiceSummaryDto
            {
                Id = i.Id,
                StripeInvoiceId = i.StripeInvoiceId,
                InvoiceNumber = i.InvoiceNumber,
                Status = i.Status.ToString(),
                Amount = i.Amount,
                AmountPaid = i.AmountPaid,
                AmountDue = i.AmountDue,
                Currency = i.Currency,
                DueDate = i.DueDate,
                PaidAt = i.PaidAt,
                IsPaid = i.IsPaid,
                IsOverdue = i.IsOverdue,
                HostedInvoiceUrl = i.HostedInvoiceUrl
            })
            .ToList();

        // 4. Get payment methods from Stripe (if customer exists)
        var paymentMethods = new List<PaymentMethodDto>();
        string? defaultPaymentMethodId = null;

        if (!string.IsNullOrWhiteSpace(customer.StripeCustomerId))
            try
            {
                var stripePMs = await _customerService.ListPaymentMethodsAsync(
                    customer.StripeCustomerId, ct);

                paymentMethods = stripePMs.Select(pm => new PaymentMethodDto
                {
                    Id = pm.Id,
                    Type = pm.Type,
                    Brand = pm.Brand,
                    Last4 = pm.Last4,
                    ExpMonth = pm.ExpMonth,
                    ExpYear = pm.ExpYear,
                    IsDefault = pm.IsDefaultForInvoices
                }).ToList();

                defaultPaymentMethodId = paymentMethods
                    .FirstOrDefault(pm => pm.IsDefault)?.Id;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to get payment methods for customer {CustomerId}",
                    customerId);
            }

        // 5. Build response
        return new CustomerDetailsResponse
        {
            Id = customer.Id,
            Email = customer.Email,
            StripeCustomerId = customer.StripeCustomerId,
            FirstName = customer.FirstName,
            LastName = customer.LastName,
            FullName = customer.FullName,
            Phone = customer.Phone,
            CompanyName = customer.CompanyName,
            Currency = customer.Currency,
            HasActiveSubscription = customer.HasActiveSubscription,
            ActiveSubscription = subscriptionSummary,
            RecentInvoices = recentInvoices,
            PaymentMethods = paymentMethods,
            DefaultPaymentMethodId = defaultPaymentMethodId
        };
    }

    // ==================== UPDATE PAYMENT METHOD ====================

    public async Task<UpdatePaymentMethodResponse> UpdatePaymentMethodAsync(
        Guid customerId,
        UpdatePaymentMethodRequest request,
        CancellationToken ct = default)
    {
        // 1. Get customer
        var customer = await _db.Customers
            .FirstOrDefaultAsync(c => c.Id == customerId, ct);

        if (customer is null)
            throw new BusinessException($"Customer not found: {customerId}");

        if (string.IsNullOrWhiteSpace(customer.StripeCustomerId))
            throw new BusinessException("Customer does not have a Stripe customer ID");

        // 2. Attach payment method to Stripe customer
        var result = await _customerService.AttachPaymentMethodAsync(
            customer.StripeCustomerId,
            request.PaymentMethodId,
            ct);

        _logger.LogInformation(
            "Attached payment method {PaymentMethodId} to customer {CustomerId}. SetAsDefault={SetAsDefault}",
            request.PaymentMethodId, customerId, result.SetAsDefaultForInvoices);

        // 3. If there's an active subscription, update it to use the new payment method
        var activeSubscription = await _db.Subscriptions
            .FirstOrDefaultAsync(s => s.CustomerId == customerId && s.IsActive, ct);

        if (activeSubscription is not null && request.SetAsDefault)
            _logger.LogInformation(
                "Payment method will be used for subscription {SubId} on next billing cycle",
                activeSubscription.Id);

        return new UpdatePaymentMethodResponse
        {
            CustomerId = customer.StripeCustomerId,
            PaymentMethodId = request.PaymentMethodId,
            SetAsDefault = result.SetAsDefaultForInvoices,
            Message = result.SetAsDefaultForInvoices
                ? "Payment method attached and set as default"
                : "Payment method attached successfully"
        };
    }

    // ==================== GET CUSTOMER PORTAL URL ====================

    public async Task<CustomerPortalResponse> GetCustomerPortalUrlAsync(
        Guid customerId,
        string returnUrl,
        CancellationToken ct = default)
    {
        // 1. Get customer
        var customer = await _db.Customers
            .FirstOrDefaultAsync(c => c.Id == customerId, ct);

        if (customer is null)
            throw new BusinessException($"Customer not found: {customerId}");

        if (string.IsNullOrWhiteSpace(customer.StripeCustomerId))
            throw new BusinessException("Customer does not have a Stripe customer ID. Please complete checkout first.");

        // 2. Create portal session
        var portalSession = await _portalService.CreatePortalSessionAsync(
            customer.StripeCustomerId,
            returnUrl,
            ct);

        _logger.LogInformation(
            "Created Stripe portal session {SessionId} for customer {CustomerId}",
            portalSession.Id, customerId);

        return new CustomerPortalResponse
        {
            PortalUrl = portalSession.Url ?? string.Empty,
            PortalSessionId = portalSession.Id,
            Message =
                "Portal session created successfully. Customer can manage subscription, payment methods, and view invoices."
        };
    }
}