using A2I.Application.Common;
using A2I.Application.Customers;
using A2I.Application.StripeAbstraction.Customers;
using A2I.Application.StripeAbstraction.Portal;
using A2I.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace A2I.Infrastructure.Customers;

public sealed class CustomerApplicationService(
    ApplicationDbContext db,
    IStripeCustomerService customerService,
    IStripePortalService portalService,
    ILogger<CustomerApplicationService> logger)
    : ICustomerApplicationService
{
    public async Task<Result<CustomerDetailsResponse>> CreateOrUpdateCustomerAsync(
        CreateOrUpdateCustomerRequest request,
        CancellationToken ct = default)
    {
        var customer = await db.Customers
            .FirstOrDefaultAsync(c => c.Id == request.CustomerId, ct);

        if (customer is null)
            return Errors.NotFound($"Customer not found: {request.CustomerId}");

        if (string.IsNullOrWhiteSpace(customer.StripeCustomerId))
        {
            var createRequest = new CreateCustomerRequest
            {
                Email = request.Email,
                Name = $"{request.FirstName} {request.LastName}".Trim(),
                Phone = request.Phone,
                Description = request.CompanyName,
                PaymentMethodId = request.PaymentMethodId,
                Metadata = request.Metadata ?? new Dictionary<string, string>()
            };

            createRequest.Metadata["internal_customer_id"] = customer.Id.ToString();

            var stripeCustomer = await customerService.CreateCustomerAsync(createRequest, ct);

            customer.StripeCustomerId = stripeCustomer.Id;

            logger.LogInformation(
                "Created Stripe customer {StripeCustomerId} for customer {CustomerId}",
                stripeCustomer.Id, customer.Id);
        }
        else
        {
            var updateRequest = new UpdateCustomerRequest
            {
                Email = request.Email,
                Name = $"{request.FirstName} {request.LastName}".Trim(),
                Phone = request.Phone,
                Description = request.CompanyName,
                DefaultPaymentMethodId = request.PaymentMethodId,
                Metadata = request.Metadata
            };

            await customerService.UpdateCustomerAsync(customer.StripeCustomerId, updateRequest, ct);

            logger.LogInformation(
                "Updated Stripe customer {StripeCustomerId} for customer {CustomerId}",
                customer.StripeCustomerId, customer.Id);
        }

        customer.Email = request.Email;
        customer.FirstName = request.FirstName;
        customer.LastName = request.LastName;
        customer.Phone = request.Phone;
        customer.CompanyName = request.CompanyName;

        await db.SaveChangesAsync(ct);

        return await GetCustomerDetailsAsync(customer.Id, ct);
    }

    public async Task<Result<CustomerDetailsResponse>> GetCustomerDetailsAsync(
        Guid customerId,
        CancellationToken ct = default)
    {
        var customer = await db.Customers
            .Include(c => c.Subscriptions.Where(s => !s.IsDeleted))
            .ThenInclude(s => s.Plan)
            .Include(c => c.Invoices.Where(i => !i.IsDeleted))
            .FirstOrDefaultAsync(c => c.Id == customerId, ct);

        if (customer is null)
            return Errors.NotFound($"Customer not found: {customerId}");

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

        // Get payment methods from Stripe (if customer exists)
        var paymentMethods = new List<PaymentMethodDto>();
        string? defaultPaymentMethodId = null;

        if (!string.IsNullOrWhiteSpace(customer.StripeCustomerId))
        {
            try
            {
                var stripePMs = await customerService.ListPaymentMethodsAsync(
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
                logger.LogWarning(ex,
                    "Failed to get payment methods for customer {CustomerId}",
                    customerId);
            }
        }

        return Result.Ok(new CustomerDetailsResponse
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
        });
    }

    public async Task<Result<UpdatePaymentMethodResponse>> UpdatePaymentMethodAsync(
        Guid customerId,
        UpdatePaymentMethodRequest request,
        CancellationToken ct = default)
    {
        var customer = await db.Customers
            .FirstOrDefaultAsync(c => c.Id == customerId, ct);

        if (customer is null)
            return Errors.NotFound($"Customer not found: {customerId}");

        if (string.IsNullOrWhiteSpace(customer.StripeCustomerId))
            return Errors.Validation("Customer does not have a Stripe customer ID");

        var result = await customerService.AttachPaymentMethodAsync(
            customer.StripeCustomerId,
            request.PaymentMethodId,
            ct);

        logger.LogInformation(
            "Attached payment method {PaymentMethodId} to customer {CustomerId}. SetAsDefault={SetAsDefault}",
            request.PaymentMethodId, customerId, result.SetAsDefaultForInvoices);

        var activeSubscription = await db.Subscriptions
            .FirstOrDefaultAsync(s => s.CustomerId == customerId && s.IsActive, ct);

        if (activeSubscription is not null && request.SetAsDefault)
            logger.LogInformation(
                "Payment method will be used for subscription {SubId} on next billing cycle",
                activeSubscription.Id);

        return Result.Ok(new UpdatePaymentMethodResponse
        {
            CustomerId = customer.StripeCustomerId,
            PaymentMethodId = request.PaymentMethodId,
            SetAsDefault = result.SetAsDefaultForInvoices,
            Message = result.SetAsDefaultForInvoices
                ? "Payment method attached and set as default"
                : "Payment method attached successfully"
        });
    }

    public async Task<Result<CustomerPortalResponse>> GetCustomerPortalUrlAsync(
        Guid customerId,
        string returnUrl,
        CancellationToken ct = default)
    {
        var customer = await db.Customers
            .FirstOrDefaultAsync(c => c.Id == customerId, ct);

        if (customer is null)
        {
            return Errors.NotFound($"Customer not found: {customerId}");
        }

        if (string.IsNullOrWhiteSpace(customer.StripeCustomerId))
        {
            return Errors.Validation("Customer does not have a Stripe customer ID");
        }

        var portalSession = await portalService.CreatePortalSessionAsync(
            customer.StripeCustomerId,
            returnUrl,
            ct);

        logger.LogInformation(
            "Created Stripe portal session {SessionId} for customer {CustomerId}",
            portalSession.Id, customerId);

        return Result.Ok(new CustomerPortalResponse
        {
            PortalUrl = portalSession.Url ?? string.Empty,
            PortalSessionId = portalSession.Id,
            Message =
                "Portal session created successfully. Customer can manage subscription, payment methods, and view invoices."
        });
    }
}