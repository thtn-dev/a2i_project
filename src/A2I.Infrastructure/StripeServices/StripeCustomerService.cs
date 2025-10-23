using System.Net;
using A2I.Application.StripeAbstraction;
using A2I.Application.StripeAbstraction.Customers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using Stripe;

namespace A2I.Infrastructure.StripeServices;

public class StripeCustomerService : IStripeCustomerService
{
    private readonly CustomerService _customerSvc;
    private readonly PaymentMethodService _pmSvc;
    private readonly IOptions<StripeOptions> _options;
    private readonly ILogger<StripeCustomerService> _logger;
    private readonly AsyncRetryPolicy _retry;
    public StripeCustomerService(IOptions<StripeOptions> options, ILogger<StripeCustomerService> logger)
    {
        _options = options;
        _logger = logger;
        StripeConfiguration.ApiKey = options.Value.SecretKey;

        _customerSvc = new CustomerService();
        _pmSvc = new PaymentMethodService();
        _retry = BuildRetryPolicy(_logger);
    }
    // ===================== Public APIs =====================

    public async Task<CustomerView> CreateCustomerAsync(CreateCustomerRequest req, CancellationToken ct = default)
    {
        try
        {
            var create = new CustomerCreateOptions
            {
                Email = req.Email,
                Name = req.Name,
                Phone = req.Phone,
                Description = req.Description,
                Metadata = req.Metadata,
                InvoiceSettings = req.PaymentMethodId is not null
                    ? new CustomerInvoiceSettingsOptions { DefaultPaymentMethod = req.PaymentMethodId }
                    : null
            };
            var reqOpts = BuildRequestOptions(idemKeySuffix: req.Email);

            var customer = await RetryAsync(
                op: "Customer.Create",
                execute: c => _customerSvc.CreateAsync(create, reqOpts, c),
                ct);

            if (!string.IsNullOrWhiteSpace(req.PaymentMethodId))
            {
                await AttachPaymentMethodCoreAsync(customer.Id, req.PaymentMethodId!, setAsDefault: true, ct);
            }

            _logger.LogInformation("Stripe Customer created: {CustomerId} ({Email})", customer.Id, customer.Email);
            return Map(customer);
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Failed to create Stripe customer: {Email}", req.Email);
            throw StripeErrorMapper.Wrap(ex, "Failed to create Stripe customer.");
        }
    }

    public async Task<CustomerView?> GetCustomerAsync(string stripeCustomerId, CancellationToken ct = default)
    {
        try
        {
            var customer = await RetryAsync(
                op: "Customer.Get",
                execute: c => _customerSvc.GetAsync(stripeCustomerId, cancellationToken: c),
                ct);

            return customer is null ? null : Map(customer);
        }
        catch (StripeException ex)
        {
            if ((int?)ex.HttpStatusCode == 404)
            {
                _logger.LogWarning("Stripe Customer not found: {CustomerId}", stripeCustomerId);
                return null;
            }

            _logger.LogError(ex, "Failed to get Stripe customer: {CustomerId}", stripeCustomerId);
            throw StripeErrorMapper.Wrap(ex, "Failed to retrieve Stripe customer.");
        }
    }

    public async Task<CustomerView> UpdateCustomerAsync(string stripeCustomerId, UpdateCustomerRequest req, CancellationToken ct = default)
    {
        try
        {
            var update = new CustomerUpdateOptions
            {
                Email = req.Email,
                Name = req.Name,
                Phone = req.Phone,
                Description = req.Description,
                Metadata = req.Metadata,
                InvoiceSettings = req.DefaultPaymentMethodId is not null
                    ? new CustomerInvoiceSettingsOptions { DefaultPaymentMethod = req.DefaultPaymentMethodId }
                    : null
            };

            var customer = await RetryAsync(
                op: "Customer.Update",
                execute: c => _customerSvc.UpdateAsync(stripeCustomerId, update, cancellationToken: c),
                ct);

            _logger.LogInformation("Stripe Customer updated: {CustomerId}", stripeCustomerId);
            return Map(customer);
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Failed to update Stripe customer: {CustomerId}", stripeCustomerId);
            throw StripeErrorMapper.Wrap(ex, "Failed to update Stripe customer.");
        }
    }

    public async Task<bool> DeleteCustomerAsync(string stripeCustomerId, CancellationToken ct = default)
    {
        try
        {
            var deleted = await RetryAsync(
                op: "Customer.Delete",
                execute: c => _customerSvc.DeleteAsync(stripeCustomerId, cancellationToken: c),
                ct);

            var ok = deleted?.Deleted == true;
            if (ok)
                _logger.LogInformation("Stripe Customer deleted: {CustomerId}", stripeCustomerId);
            else
                _logger.LogWarning("Stripe Customer delete returned not-deleted: {CustomerId}", stripeCustomerId);

            return ok;
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Failed to delete Stripe customer: {CustomerId}", stripeCustomerId);
            throw StripeErrorMapper.Wrap(ex, "Failed to delete Stripe customer.");
        }
    }

    public async Task<AttachPaymentMethodResult> AttachPaymentMethodAsync(string customerId, string paymentMethodId, CancellationToken ct = default)
    {
        try
        {
            await AttachPaymentMethodCoreAsync(customerId, paymentMethodId, setAsDefault: false, ct);

            var customer = await RetryAsync(
                op: "Customer.GetForDefaultPM",
                execute: c => _customerSvc.GetAsync(customerId, cancellationToken: c),
                ct);

            var currentDefault = customer?.InvoiceSettings?.DefaultPaymentMethodId;
            var setDefault = string.IsNullOrWhiteSpace(currentDefault);

            if (setDefault)
            {
                await RetryAsync(
                    op: "Customer.UpdateDefaultPM",
                    execute: c => _customerSvc.UpdateAsync(customerId, new CustomerUpdateOptions
                    {
                        InvoiceSettings = new CustomerInvoiceSettingsOptions { DefaultPaymentMethod = paymentMethodId }
                    }, cancellationToken: c),
                    ct);
            }

            _logger.LogInformation("PaymentMethod {PaymentMethodId} attached to {CustomerId}, SetAsDefault={IsDefault}",
                paymentMethodId, customerId, setDefault);

            return new AttachPaymentMethodResult
            {
                CustomerId = customerId,
                PaymentMethodId = paymentMethodId,
                SetAsDefaultForInvoices = setDefault
            };
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Failed to attach PaymentMethod {PaymentMethodId} to {CustomerId}", paymentMethodId, customerId);
            throw StripeErrorMapper.Wrap(ex, "Failed to attach payment method to Stripe customer.");
        }
    }

    public async Task<IReadOnlyList<PaymentMethodView>> ListPaymentMethodsAsync(string customerId, CancellationToken ct = default)
    {
        try
        {
            var list = await RetryAsync(
                op: "PaymentMethod.List",
                execute: c => _pmSvc.ListAsync(new PaymentMethodListOptions
                {
                    Customer = customerId,
                    Type = "card"
                }, cancellationToken: c),
                ct);

            var customer = await RetryAsync(
                op: "Customer.GetForListPM",
                execute: c => _customerSvc.GetAsync(customerId, cancellationToken: c),
                ct);

            var defaultPmId = customer?.InvoiceSettings?.DefaultPaymentMethodId;

            var result = list.Data.Select(pm => Map(pm, pm.Id == defaultPmId)).ToList();
            _logger.LogInformation("Listed {Count} payment methods for {CustomerId}", result.Count, customerId);
            return result;
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Failed to list payment methods for {CustomerId}", customerId);
            throw StripeErrorMapper.Wrap(ex, "Failed to list Stripe payment methods.");
        }
    }

    // ---------- helpers ----------

    private async Task AttachPaymentMethodCoreAsync(string customerId, string paymentMethodId, bool setAsDefault, CancellationToken ct)
    {
        // Attach
        await RetryAsync(
            op: "PaymentMethod.Attach",
            execute: c => _pmSvc.AttachAsync(paymentMethodId, new PaymentMethodAttachOptions
            {
                Customer = customerId
            }, cancellationToken: c),
            ct);

        if (setAsDefault)
        {
            await RetryAsync(
                op: "Customer.UpdateDefaultPM.AfterAttach",
                execute: c => _customerSvc.UpdateAsync(customerId, new CustomerUpdateOptions
                {
                    InvoiceSettings = new CustomerInvoiceSettingsOptions { DefaultPaymentMethod = paymentMethodId }
                }, cancellationToken: c),
                ct);
        }
    }

    private static CustomerView Map(Customer c) => new()
    {
        Id = c.Id,
        Email = c.Email,
        Name = c.Name,
        Phone = c.Phone,
        Description = c.Description,
        DefaultPaymentMethodId = c.InvoiceSettings?.DefaultPaymentMethodId,
        Metadata = c.Metadata,
        Deleted = c.Deleted ?? false
    };

    private static PaymentMethodView Map(PaymentMethod pm, bool isDefault) => new()
    {
        Id = pm.Id,
        Type = pm.Type,
        Brand = pm.Card?.Brand,
        Last4 = pm.Card?.Last4,
        ExpMonth = pm.Card?.ExpMonth,
        ExpYear = pm.Card?.ExpYear,
        IsDefaultForInvoices = isDefault
    };

    private RequestOptions? BuildRequestOptions(string? idemKeySuffix = null)
    {
        // For write operations we can add an Idempotency-Key (optional but recommended)
        var prefix = _options.Value.IdempotencyPrefix;
        var key = (idemKeySuffix is null) ? null : $"{prefix}{idemKeySuffix}:{Guid.NewGuid():N}";
        return key is null ? null : new RequestOptions { IdempotencyKey = key };
    }
    
    // -------- Polly: retry policy + classification --------

    private static AsyncRetryPolicy BuildRetryPolicy(ILogger logger)
    {
        // 5 attempts, exponential backoff (0.2s, 0.4s, 0.8s, 1.6s, 3.2s) + jitter
        var delays = Enumerable.Range(0, 5)
            .Select(i =>
            {
                var baseDelay = TimeSpan.FromMilliseconds(200 * Math.Pow(2, i));
                var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, 120));
                return baseDelay + jitter;
            });

        return Policy
            .Handle<StripeException>(IsTransient)
            .Or<HttpRequestException>()
            .Or<TaskCanceledException>() // timeout/cancel (Stripe SDK uses HttpClient underneath)
            .WaitAndRetryAsync(
                delays,
                (ex, delay, attempt, _) =>
                {
                    var (code, reqId, stripeCode, http) = ExtractStripeError(ex);
                    logger.LogWarning(ex,
                        "Retrying Stripe operation (attempt {Attempt}) after {Delay}. Http={HttpStatus} StripeCode={StripeCode} RequestId={RequestId} ErrorCode={Code}",
                        attempt, delay, http, stripeCode, reqId, code);
                });
    }

    private static bool IsTransient(StripeException ex)
    {
        var status = (HttpStatusCode?)ex.HttpStatusCode;
        // Throttling & server errors
        if (status is HttpStatusCode.TooManyRequests or
            HttpStatusCode.InternalServerError or
            HttpStatusCode.BadGateway or
            HttpStatusCode.ServiceUnavailable or
            HttpStatusCode.GatewayTimeout)
        {
            return true;
        }
        // Some Stripe error types are safe to retry (api_connection_error, rate_limit_error)
        var type = ex.StripeError?.Type?.ToLowerInvariant();
        if (type is "api_connection_error" or "rate_limit_error") return true;

        return false;
    }

    private static (string? code, string? requestId, string? stripeCode, int? http) ExtractStripeError(Exception ex)
    {
        if (ex is StripeException se)
        {
            return (se.StripeError?.Code, se.Source, se.StripeError?.Type, (int?)se.HttpStatusCode);
        }
        return (null, null, null, null);
    }

    private Task<T> RetryAsync<T>(string op, Func<CancellationToken, Task<T>> execute, CancellationToken ct)
        => _retry.ExecuteAsync(async innerCt =>
        {
            using var scope = _logger.BeginScope(new Dictionary<string, object?>
            {
                ["stripe_op"] = op
            });

            var res = await execute(innerCt);
            _logger.LogDebug("Stripe op {Op} succeeded", op);
            return res;
        }, ct);
}