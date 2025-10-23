using System.Net;
using A2I.Application.StripeAbstraction;
using A2I.Application.StripeAbstraction.Checkout;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using Stripe;
using Stripe.Checkout;

namespace A2I.Infrastructure.StripeServices;

public sealed class StripeCheckoutService : IStripeCheckoutService
{
    private readonly SessionService _sessionSvc;
    private readonly IOptions<StripeOptions> _options;
    private readonly ILogger<StripeCheckoutService> _logger;
    private readonly AsyncRetryPolicy _retry;

    public StripeCheckoutService(IOptions<StripeOptions> options, ILogger<StripeCheckoutService> logger)
    {
        _options = options;
        _logger = logger;

        StripeConfiguration.ApiKey = options.Value.SecretKey;
        _sessionSvc = new SessionService();
        _retry = BuildRetryPolicy(logger);
    }

    public async Task<CheckoutSessionView> CreateCheckoutSessionAsync(CreateCheckoutRequest req, CancellationToken ct = default)
    {
        try
        {
            var create = new SessionCreateOptions
            {
                Mode = "subscription",
                SuccessUrl = req.SuccessUrl,
                CancelUrl = req.CancelUrl,
                AllowPromotionCodes = req.AllowPromotionCodes,
                BillingAddressCollection = req.BillingAddressCollection,
                LineItems = [new SessionLineItemOptions { Price = req.PriceId, Quantity = req.Quantity }],
                PaymentMethodTypes = req.PaymentMethodTypes ?? ["card"],
                Metadata = req.Metadata
            };

            if (!string.IsNullOrWhiteSpace(req.CustomerId))
            {
                create.Customer = req.CustomerId;
            }
            else if (!string.IsNullOrWhiteSpace(req.CustomerEmail))
            {
                create.CustomerEmail = req.CustomerEmail;
            }

            if (req.TrialPeriodDays.HasValue)
            {
                create.SubscriptionData = new SessionSubscriptionDataOptions
                {
                    TrialPeriodDays = req.TrialPeriodDays
                };
            }

            var reqOpts = BuildRequestOptions("checkout_create", req.CustomerId ?? req.CustomerEmail);

            var session = await RetryAsync("Checkout.Create",
                c => _sessionSvc.CreateAsync(create, reqOpts, c), ct);

            _logger.LogInformation("Created checkout session {SessionId} for customer {CustomerOrEmail}", session.Id, req.CustomerId ?? req.CustomerEmail);
            return Map(session);
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Failed to create checkout session for price {PriceId}", req.PriceId);
            throw StripeErrorMapper.Wrap(ex, "Failed to create Stripe checkout session.");
        }
    }

    public async Task<CheckoutSessionView?> GetCheckoutSessionAsync(string sessionId, CancellationToken ct = default)
    {
        try
        {
            var s = await RetryAsync("Checkout.Get",
                c => _sessionSvc.GetAsync(sessionId, cancellationToken: c), ct);
            return s is null ? null : Map(s);
        }
        catch (StripeException ex)
        {
            if ((int?)ex.HttpStatusCode == 404)
            {
                _logger.LogWarning("Checkout session not found: {SessionId}", sessionId);
                return null;
            }
            _logger.LogError(ex, "Failed to get checkout session {SessionId}", sessionId);
            throw StripeErrorMapper.Wrap(ex, "Failed to retrieve Stripe checkout session.");
        }
    }

    public async Task<bool> ExpireCheckoutSessionAsync(string sessionId, CancellationToken ct = default)
    {
        try
        {
            var expired = await RetryAsync("Checkout.Expire",
                c => _sessionSvc.ExpireAsync(sessionId, new SessionExpireOptions(), requestOptions: null, cancellationToken: c), ct);

            var ok = string.Equals(expired?.Status, "expired", StringComparison.OrdinalIgnoreCase);
            if (ok)
                _logger.LogInformation("Expired checkout session {SessionId}", sessionId);
            else
                _logger.LogWarning("Expire attempted but session not in 'expired' status {SessionId} -> {Status}", sessionId, expired?.Status);

            return ok;
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Failed to expire checkout session {SessionId}", sessionId);
            throw StripeErrorMapper.Wrap(ex, "Failed to expire Stripe checkout session.");
        }
    }

    // --- helpers ---

    private static CheckoutSessionView Map(Session s) => new()
    {
        Id = s.Id,
        Url = s.Url,
        CustomerId = s.CustomerId,
        SubscriptionId = s.SubscriptionId,
        Status = s.Status,
        PaymentStatus = s.PaymentStatus,
        ExpiresAt = s.ExpiresAt
    };

    private RequestOptions BuildRequestOptions(string action, string? keyHint) =>
        new() { IdempotencyKey = $"{_options.Value.IdempotencyPrefix}{action}:{keyHint}:{Guid.NewGuid():N}" };

    private static AsyncRetryPolicy BuildRetryPolicy(ILogger logger)
    {
        var delays = Enumerable.Range(0, 5)
            .Select(i => TimeSpan.FromMilliseconds(200 * Math.Pow(2, i)) + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 120)));

        return Policy
            .Handle<StripeException>(IsTransient)
            .Or<HttpRequestException>()
            .Or<TaskCanceledException>()
            .WaitAndRetryAsync(delays, (ex, delay, attempt, _) =>
            {
                var (code, reqId, stripeCode, http) = ExtractStripeError(ex);
                logger.LogWarning(ex, "Retrying Checkout op (attempt {Attempt}) after {Delay}. Http={Http} StripeCode={StripeCode} ReqId={ReqId} Code={Code}",
                    attempt, delay, http, stripeCode, reqId, code);
            });
    }

    private static bool IsTransient(StripeException ex)
    {
        var status = (HttpStatusCode?)ex.HttpStatusCode;
        return status is HttpStatusCode.TooManyRequests or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout
            || string.Equals(ex.StripeError?.Type, "api_connection_error", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ex.StripeError?.Type, "rate_limit_error", StringComparison.OrdinalIgnoreCase);
    }

    private static (string? code, string? requestId, string? stripeCode, int? http) ExtractStripeError(Exception ex)
    {
        if (ex is StripeException se)
            return (se.StripeError?.Code, se.Source, se.StripeError?.Type, (int?)se.HttpStatusCode);
        return (null, null, null, null);
    }

    private Task<T> RetryAsync<T>(string op, Func<CancellationToken, Task<T>> execute, CancellationToken ct)
        => _retry.ExecuteAsync(async innerCt =>
        {
            using var scope = _logger.BeginScope(new Dictionary<string, object?> { ["stripe_op"] = op });
            var res = await execute(innerCt);
            _logger.LogDebug("Stripe op {Op} succeeded", op);
            return res;
        }, ct);
}