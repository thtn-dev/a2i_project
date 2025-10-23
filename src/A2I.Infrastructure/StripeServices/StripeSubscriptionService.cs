
using System.Net;
using A2I.Application.StripeAbstraction;
using A2I.Application.StripeAbstraction.Subscriptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using Stripe;

namespace A2I.Infrastructure.StripeServices;

public sealed class StripeSubscriptionService : IStripeSubscriptionService
{
    private readonly SubscriptionService _subSvc;
    private readonly IOptions<StripeOptions> _options;
    private readonly ILogger<StripeSubscriptionService> _logger;
    private readonly AsyncRetryPolicy _retry;

    public StripeSubscriptionService(
        IOptions<StripeOptions> options,
        ILogger<StripeSubscriptionService> logger)
    {
        _options = options;
        _logger = logger;

        StripeConfiguration.ApiKey = options.Value.SecretKey;

        _subSvc = new SubscriptionService();

        _retry = BuildRetryPolicy(_logger);
    }

    // ---------------------------- Create ----------------------------

    public async Task<SubscriptionView> CreateSubscriptionAsync(CreateSubscriptionRequest req, CancellationToken ct = default)
    {
        try
        {
            var create = new SubscriptionCreateOptions
            {
                Customer = req.CustomerId,
                Items = [new SubscriptionItemOptions { Price = req.PriceId, Quantity = req.Quantity }],
                Metadata = req.Metadata,
                ProrationBehavior = MapProration(req.Proration),
            };

            if (req.PromotionCode is not null)
            {
                // TODO: implement
            }
                

            if (req.TrialPeriodDays.HasValue)
                create.TrialPeriodDays = req.TrialPeriodDays;
            else if (req.TrialEnd.HasValue)
                create.TrialEnd = req.TrialEnd.Value.DateTime;

            var reqOpts = BuildRequestOptions("sub_create", req.CustomerId);

            var sub = await RetryAsync("Subscription.Create",
                c => _subSvc.CreateAsync(create, reqOpts, c), ct);

            _logger.LogInformation("Created subscription {SubId} for customer {CustomerId} price {PriceId}",
                sub.Id, req.CustomerId, req.PriceId);

            return Map(sub);
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Failed to create subscription for {CustomerId} / {PriceId}", req.CustomerId, req.PriceId);
            throw StripeErrorMapper.Wrap(ex, "Failed to create Stripe subscription.");
        }
    }

    // ---------------------------- Get ----------------------------

    public async Task<SubscriptionView?> GetSubscriptionAsync(string subscriptionId, CancellationToken ct = default)
    {
        try
        {
            var sub = await RetryAsync("Subscription.Get",
                c => _subSvc.GetAsync(subscriptionId, cancellationToken: c), ct);
            return sub is null ? null : Map(sub);
        }
        catch (StripeException ex)
        {
            if ((int?)ex.HttpStatusCode == 404)
            {
                _logger.LogWarning("Subscription not found: {SubId}", subscriptionId);
                return null;
            }
            _logger.LogError(ex, "Failed to get subscription {SubId}", subscriptionId);
            throw StripeErrorMapper.Wrap(ex, "Failed to retrieve Stripe subscription.");
        }
    }

    // ---------------------------- Update (quantity/metadata/trial) ----------------------------

    public async Task<SubscriptionView> UpdateSubscriptionAsync(string subscriptionId, UpdateSubscriptionRequest req, CancellationToken ct = default)
    {
        try
        {
            // Lấy sub để lấy item id hiện tại khi cần update quantity
            var existing = await RetryAsync("Subscription.GetForUpdate",
                c => _subSvc.GetAsync(subscriptionId, cancellationToken: c), ct);

            var firstItemId = existing.Items?.Data?.FirstOrDefault()?.Id;

            var update = new SubscriptionUpdateOptions
            {
                Metadata = req.Metadata,
                ProrationBehavior = MapProration(req.Proration),
            };

            if (req.Quantity.HasValue && firstItemId is not null)
            {
                update.Items = new List<SubscriptionItemOptions>
                {
                    new() { Id = firstItemId, Quantity = req.Quantity }
                };
            }

            if (req.TrialEnd.HasValue)
            {
                update.TrialEnd = req.TrialEnd.Value.DateTime;
            }

            var sub = await RetryAsync("Subscription.Update",
                c => _subSvc.UpdateAsync(subscriptionId, update, cancellationToken: c), ct);

            _logger.LogInformation("Updated subscription {SubId}", subscriptionId);
            return Map(sub);
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Failed to update subscription {SubId}", subscriptionId);
            throw StripeErrorMapper.Wrap(ex, "Failed to update Stripe subscription.");
        }
    }

    // ---------------------------- Cancel ----------------------------

    public async Task<SubscriptionView> CancelSubscriptionAsync(string subscriptionId, bool immediately, CancellationToken ct = default)
    {
        try
        {
            if (immediately)
            {
                // Cancel ngay lập tức (có thể chọn InvoiceNow/Prorate; ở đây dùng mặc định Stripe)
                var canceled = await RetryAsync("Subscription.CancelNow",
                    c => _subSvc.CancelAsync(subscriptionId, new SubscriptionCancelOptions
                    {
                        // Tùy nhu cầu có thể bật:
                        // InvoiceNow = true,
                        // Prorate = true
                    }, cancellationToken: c), ct);

                _logger.LogInformation("Canceled subscription immediately: {SubId}", subscriptionId);
                return Map(canceled);
            }
            else
            {
                // Set CancelAtPeriodEnd = true
                var updated = await RetryAsync("Subscription.CancelAtPeriodEnd",
                    c => _subSvc.UpdateAsync(subscriptionId, new SubscriptionUpdateOptions
                    {
                        CancelAtPeriodEnd = true
                    }, cancellationToken: c), ct);

                _logger.LogInformation("Marked subscription to cancel at period end: {SubId}", subscriptionId);
                return Map(updated);
            }
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Failed to cancel subscription {SubId} (immediately={Immediately})", subscriptionId, immediately);
            throw StripeErrorMapper.Wrap(ex, "Failed to cancel Stripe subscription.");
        }
    }

    // ---------------------------- Reactivate (un-cancel at period end) ----------------------------

    public async Task<SubscriptionView> ReactivateSubscriptionAsync(string subscriptionId, CancellationToken ct = default)
    {
        try
        {
            // Nếu chưa kết thúc và đang set CancelAtPeriodEnd = true thì bỏ cờ này
            var updated = await RetryAsync("Subscription.Reactivate",
                c => _subSvc.UpdateAsync(subscriptionId, new SubscriptionUpdateOptions
                {
                    CancelAtPeriodEnd = false
                }, cancellationToken: c), ct);

            _logger.LogInformation("Reactivated subscription (unset cancel_at_period_end): {SubId}", subscriptionId);
            return Map(updated);
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Failed to reactivate subscription {SubId}", subscriptionId);
            throw StripeErrorMapper.Wrap(ex, "Failed to reactivate Stripe subscription.");
        }
    }

    // ---------------------------- Change Plan (price) ----------------------------

    public async Task<SubscriptionView> ChangeSubscriptionPlanAsync(string subscriptionId, string newPriceId, CancellationToken ct = default)
    {
        try
        {
            var existing = await RetryAsync("Subscription.GetForChangePlan",
                c => _subSvc.GetAsync(subscriptionId, cancellationToken: c), ct);

            var currentItem = existing.Items?.Data?.FirstOrDefault();
            if (currentItem is null)
                throw new StripeServiceException("Subscription has no items to update.", new InvalidOperationException());

            var update = new SubscriptionUpdateOptions
            {
                // Mặc định: tạo prorations. Cho phép caller điều chỉnh qua một overload khác nếu cần
                ProrationBehavior = "create_prorations",
                Items = new List<SubscriptionItemOptions>
                {
                    new()
                    {
                        Id = currentItem.Id,
                        Price = newPriceId,
                        Quantity = currentItem.Quantity
                    }
                }
            };

            var sub = await RetryAsync("Subscription.ChangePlan",
                c => _subSvc.UpdateAsync(subscriptionId, update, cancellationToken: c), ct);

            _logger.LogInformation("Changed subscription {SubId} plan to {PriceId}", subscriptionId, newPriceId);
            return Map(sub);
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Failed to change plan for subscription {SubId} -> {PriceId}", subscriptionId, newPriceId);
            throw StripeErrorMapper.Wrap(ex, "Failed to change Stripe subscription plan.");
        }
    }

    // ---------------------------- Pause / Resume ----------------------------

    public async Task<SubscriptionView> PauseSubscriptionAsync(string subscriptionId, PauseBehavior behavior = PauseBehavior.KeepAsDraft, CancellationToken ct = default)
    {
        try
        {
            var pause = new SubscriptionPauseCollectionOptions
            {
                Behavior = behavior switch
                {
                    PauseBehavior.MarkUncollectible => "mark_uncollectible",
                    PauseBehavior.Void => "void",
                    _ => "keep_as_draft"
                }
            };

            var updated = await RetryAsync("Subscription.Pause",
                c => _subSvc.UpdateAsync(subscriptionId, new SubscriptionUpdateOptions
                {
                    PauseCollection = pause
                }, cancellationToken: c), ct);

            _logger.LogInformation("Paused subscription {SubId} with behavior {Behavior}", subscriptionId, pause.Behavior);
            return Map(updated);
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Failed to pause subscription {SubId}", subscriptionId);
            throw StripeErrorMapper.Wrap(ex, "Failed to pause Stripe subscription.");
        }
    }

    public async Task<SubscriptionView> ResumeSubscriptionAsync(string subscriptionId, CancellationToken ct = default)
    {
        try
        {
            var updated = await RetryAsync("Subscription.Resume",
                c => _subSvc.UpdateAsync(subscriptionId, new SubscriptionUpdateOptions
                {
                    PauseCollection = null
                }, cancellationToken: c), ct);

            _logger.LogInformation("Resumed subscription {SubId}", subscriptionId);
            return Map(updated);
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Failed to resume subscription {SubId}", subscriptionId);
            throw StripeErrorMapper.Wrap(ex, "Failed to resume Stripe subscription.");
        }
    }

    // ---------------------------- Helpers ----------------------------

    private static string MapProration(ProrationMode mode) => mode switch
    {
        ProrationMode.None => "none",
        _ => "create_prorations"
    };

    private static SubscriptionView Map(Subscription s)
    {
        var firstItem = s.Items?.Data?.FirstOrDefault();
        return new SubscriptionView
        {
            Id = s.Id,
            CustomerId = s.CustomerId,
            PriceId = firstItem?.Price?.Id ?? firstItem?.Plan?.Id ?? "", // fallback
            Status = s.Status,
            CurrentPeriodStart = s.StartDate,
            CurrentPeriodEnd = s.EndedAt,
            CancelAtPeriodEnd = s.CancelAtPeriodEnd,
            CancelAt = s.CancelAt,
            CanceledAt = s.CanceledAt,
            TrialStart = s.TrialStart,
            TrialEnd = s.TrialEnd,
            Quantity = firstItem?.Quantity ?? 1,
            Metadata = s.Metadata,
            LatestInvoiceId = s.LatestInvoiceId
        };
    }

    private RequestOptions BuildRequestOptions(string action, string? keyHint = null)
    {
        var prefix = string.IsNullOrWhiteSpace(_options.Value.IdempotencyPrefix) ? "sub_" : _options.Value.IdempotencyPrefix;
        var idem = $"{prefix}{action}:{keyHint}:{Guid.NewGuid():N}";
        return new RequestOptions { IdempotencyKey = idem };
    }

    // -------- Polly retry policy --------

    private static AsyncRetryPolicy BuildRetryPolicy(ILogger logger)
    {
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
            .Or<TaskCanceledException>()
            .WaitAndRetryAsync(delays, (ex, delay, attempt, _) =>
            {
                var (code, reqId, stripeCode, http) = ExtractStripeError(ex);
                logger.LogWarning(ex,
                    "Retrying Stripe Subscription op (attempt {Attempt}) after {Delay}. Http={HttpStatus} StripeCode={StripeCode} RequestId={RequestId} ErrorCode={Code}",
                    attempt, delay, http, stripeCode, reqId, code);
            });
    }

    private static bool IsTransient(StripeException ex)
    {
        var status = (HttpStatusCode?)ex.HttpStatusCode;
        if (status is HttpStatusCode.TooManyRequests
                 or HttpStatusCode.InternalServerError
                 or HttpStatusCode.BadGateway
                 or HttpStatusCode.ServiceUnavailable
                 or HttpStatusCode.GatewayTimeout)
            return true;

        var type = ex.StripeError?.Type?.ToLowerInvariant();
        return type is "api_connection_error" or "rate_limit_error";
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

