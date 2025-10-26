using A2I.Application.StripeAbstraction;
using A2I.Application.StripeAbstraction.Webhooks;
using Microsoft.Extensions.Logging;
using Stripe;

namespace A2I.Infrastructure.StripeServices;

public sealed class StripeWebhookService(ILogger<StripeWebhookService> logger) : IStripeWebhookService
{
    public Event ConstructEvent(string json, string signature, string secret)
    {
        try
        {
            var stripeEvent = EventUtility.ConstructEvent(json, signature, secret, throwOnApiVersionMismatch: false);
            logger.LogInformation("Webhook verified. Event {EventId} type {Type}", stripeEvent.Id, stripeEvent.Type);
            return stripeEvent;
        }
        catch (StripeException ex)
        {
            logger.LogWarning(ex, "Invalid Stripe webhook signature");
            throw StripeErrorMapper.Wrap(ex, "Invalid Stripe webhook signature.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to construct Stripe webhook event");
            throw new StripeServiceException("Failed to construct Stripe webhook event.", ex);
        }
    }
}