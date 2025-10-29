using A2I.Application.StripeAbstraction.Webhooks;
using A2I.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Stripe;

namespace A2I.Infrastructure.StripeServices;

public abstract class WebhookEventHandlerBase : IWebhookEventHandler
{
    protected readonly ApplicationDbContext Db;
    protected readonly ILogger Logger;

    protected WebhookEventHandlerBase(ApplicationDbContext db, ILogger logger)
    {
        Db = db;
        Logger = logger;
    }

    public abstract string EventType { get; }

    public async Task<WebhookHandlerResult> HandleAsync(Event stripeEvent, CancellationToken ct = default)
    {
        try
        {
            Logger.LogInformation(
                "Processing webhook {EventType} {EventId}",
                EventType, stripeEvent.Id);

            var result = await HandleCoreAsync(stripeEvent, ct);

            Logger.LogInformation(
                "Webhook {EventId} processed: {Success} - {Message}",
                stripeEvent.Id, result.Success, result.Message);

            return result;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex,
                "Failed to process webhook {EventType} {EventId}",
                EventType, stripeEvent.Id);

            return new WebhookHandlerResult(
                false,
                ex.Message,
                IsRetryableError(ex)
            );
        }
    }

    protected abstract Task<WebhookHandlerResult> HandleCoreAsync(
        Event stripeEvent,
        CancellationToken ct);

    protected virtual bool IsRetryableError(Exception ex)
    {
        // Transient errors: DB timeouts, network issues
        return ex is DbUpdateException
               || ex is TimeoutException
               || ex is HttpRequestException;
    }
}