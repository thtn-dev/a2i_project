using System.Net;
using A2I.Application.StripeAbstraction;
using A2I.Application.StripeAbstraction.Portal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using Stripe;
using Stripe.BillingPortal;

namespace A2I.Infrastructure.StripeServices;

public sealed class StripePortalService : IStripePortalService
{
    private readonly ILogger<StripePortalService> _logger;
    private readonly IOptions<StripeOptions> _options;
    private readonly SessionService _portalSvc;
    private readonly AsyncRetryPolicy _retry;

    public StripePortalService(IOptions<StripeOptions> options, ILogger<StripePortalService> logger)
    {
        _options = options;
        _logger = logger;

        StripeConfiguration.ApiKey = options.Value.SecretKey;
        _portalSvc = new SessionService();
        _retry = BuildRetryPolicy(logger);
    }

    public async Task<PortalSessionView> CreatePortalSessionAsync(string customerId, string returnUrl,
        CancellationToken ct = default)
    {
        try
        {
            var create = new SessionCreateOptions
            {
                Customer = customerId,
                ReturnUrl = returnUrl
            };

            var reqOpts = new RequestOptions
            {
                IdempotencyKey = $"{_options.Value.IdempotencyPrefix}create:{customerId}:{Guid.NewGuid():N}"
            };

            var session = await RetryAsync("Portal.Create",
                c => _portalSvc.CreateAsync(create, reqOpts, c), ct);

            _logger.LogInformation("Created Billing Portal session {PortalId} for {CustomerId}", session.Id,
                customerId);
            return new PortalSessionView { Id = session.Id, Url = session.Url };
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Failed to create Billing Portal session for {CustomerId}", customerId);
            throw StripeErrorMapper.Wrap(ex, "Failed to create Stripe Billing Portal session.");
        }
    }

    private static AsyncRetryPolicy BuildRetryPolicy(ILogger logger)
    {
        var delays = Enumerable.Range(0, 5)
            .Select(i =>
                TimeSpan.FromMilliseconds(200 * Math.Pow(2, i)) +
                TimeSpan.FromMilliseconds(Random.Shared.Next(0, 120)));

        return Policy
            .Handle<StripeException>(IsTransient)
            .Or<HttpRequestException>()
            .Or<TaskCanceledException>()
            .WaitAndRetryAsync(delays, (ex, delay, attempt, _) =>
            {
                var (code, reqId, stripeCode, http) = ExtractStripeError(ex);
                logger.LogWarning(ex,
                    "Retrying Portal op (attempt {Attempt}) after {Delay}. Http={Http} StripeCode={StripeCode} ReqId={ReqId} Code={Code}",
                    attempt, delay, http, stripeCode, reqId, code);
            });
    }

    private static bool IsTransient(StripeException ex)
    {
        var status = (HttpStatusCode?)ex.HttpStatusCode;
        return status is HttpStatusCode.TooManyRequests or HttpStatusCode.InternalServerError
                   or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout
               || string.Equals(ex.StripeError?.Type, "api_error", StringComparison.OrdinalIgnoreCase);
    }

    private static (string? code, string? requestId, string? stripeCode, int? http) ExtractStripeError(Exception ex)
    {
        if (ex is StripeException se)
            return (se.StripeError?.Code, se.Source, se.StripeError?.Type, (int?)se.HttpStatusCode);
        return (null, null, null, null);
    }

    private Task<T> RetryAsync<T>(string op, Func<CancellationToken, Task<T>> execute, CancellationToken ct)
    {
        return _retry.ExecuteAsync(async innerCt =>
        {
            using var scope = _logger.BeginScope(new Dictionary<string, object?> { ["stripe_op"] = op });
            var res = await execute(innerCt);
            _logger.LogDebug("Stripe op {Op} succeeded", op);
            return res;
        }, ct);
    }
}