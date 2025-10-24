using A2I.Application.StripeAbstraction.Webhooks;
using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stripe;

namespace A2I.WebAPI.Controllers;

[ApiController]
[Route("api/webhooks")]
public class StripeWebhookController : ControllerBase
{
    private readonly IStripeWebhookService _webhookService;
    private readonly IWebhookEventDispatcher _dispatcher;
    private readonly IEventIdempotencyStore _idempotencyStore;
    private readonly IConfiguration _config;
    private readonly ILogger<StripeWebhookController> _logger;
    
    public StripeWebhookController(
        IStripeWebhookService webhookService,
        IWebhookEventDispatcher dispatcher,
        IEventIdempotencyStore idempotencyStore,
        IConfiguration config,
        ILogger<StripeWebhookController> logger)
    {
        _webhookService = webhookService;
        _dispatcher = dispatcher;
        _idempotencyStore = idempotencyStore;
        _config = config;
        _logger = logger;
    }
    
    [HttpPost("stripe")]
    [AllowAnonymous]
    public async Task<IActionResult> HandleStripeWebhook(CancellationToken ct)
    {
        var eventId = "unknown";
        
        try
        {
            // 1. Read raw body
            using var reader = new StreamReader(Request.Body);
            var json = await reader.ReadToEndAsync(ct);
            
            var signature = Request.Headers["Stripe-Signature"].FirstOrDefault();
            if (string.IsNullOrEmpty(signature))
            {
                _logger.LogWarning("Webhook received without Stripe signature");
                return BadRequest(new { error = "Missing Stripe-Signature header" });
            }
            
            var secret = _config["Stripe:WebhookSecret"] 
                ?? throw new InvalidOperationException("Stripe:WebhookSecret not configured");
            
            // 2. Validate signature & construct event
            var stripeEvent = _webhookService.ConstructEventAsync(json, signature, secret);
            eventId = stripeEvent.Id;
            
            _logger.LogInformation(
                "Received Stripe webhook: {EventType} {EventId}",
                stripeEvent.Type, eventId);
            
            // 3. Idempotency check
            if (await _idempotencyStore.HasProcessedAsync(eventId, ct))
            {
                _logger.LogInformation(
                    "Webhook {EventId} already processed (duplicate)",
                    eventId);
                
                return Ok(new { received = true, message = "Event already processed" });
            }
            
            // 4. Mark as processing (prevents duplicate)
            await _idempotencyStore.MarkProcessedAsync(eventId, ct);
            
            // 5. Dispatch to handler (synchronously for now, can queue later)
            var result = await _dispatcher.DispatchAsync(stripeEvent, ct);
            
            // 6. Update webhook event status
            await _idempotencyStore.UpdateEventStatusAsync(
                eventId,
                stripeEvent.Type,
                result.Success ? "processed" : "failed",
                result.Success ? null : result.Message,
                ct);
            
            // 7. Queue retry if needed
            if (result is { Success: false, RequiresRetry: true })
            {
                BackgroundJob.Schedule(
                    () => RetryWebhookAsync(eventId, json, stripeEvent.Type),
                    TimeSpan.FromMinutes(5));
                
                _logger.LogWarning(
                    "Webhook {EventId} failed, scheduled for retry in 5 minutes",
                    eventId);
            }
            
            // 8. Always return 200 OK to Stripe
            return Ok(new
            {
                received = true,
                eventId,
                eventType = stripeEvent.Type,
                success = result.Success,
                message = result.Message
            });
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe webhook signature validation failed");
            return BadRequest(new { error = "Invalid signature", eventId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error processing webhook {EventId}", eventId);
            
            // Still return 200 to prevent Stripe from retrying indefinitely
            return Ok(new
            {
                received = true,
                eventId,
                success = false,
                error = ex.Message
            });
        }
    }
    
    // Hangfire background job for retry
    private async Task RetryWebhookAsync(string eventId, string json, string eventType)
    {
        try
        {
            _logger.LogInformation("Retrying webhook {EventId} ({EventType})", eventId, eventType);
            
            // Re-construct event from stored JSON (skip signature validation for retry)
            var stripeEvent = Newtonsoft.Json.JsonConvert.DeserializeObject<Event>(json);
            
            if (stripeEvent != null)
            {
                var result = await _dispatcher.DispatchAsync(stripeEvent, CancellationToken.None);
                
                await _idempotencyStore.UpdateEventStatusAsync(
                    eventId,
                    eventType,
                    result.Success ? "processed" : "failed",
                    result.Success ? null : result.Message);
                
                _logger.LogInformation(
                    "Webhook retry {EventId}: {Success}",
                    eventId, result.Success);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retry webhook {EventId}", eventId);
        }
    }
}