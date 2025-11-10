using A2I.Application.Common;
using A2I.Application.Subscriptions;
using A2I.WebAPI.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace A2I.WebAPI.Endpoints.Subscriptions;

/// <summary>
///     Subscription management endpoints
/// </summary>
public static class SubscriptionEndpoints
{
    public static RouteGroupBuilder MapSubscriptionEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/start", StartSubscription)
            .WithName("StartSubscription")
            .WithApiMetadata(
                "Start new subscription",
                "Creates a Stripe checkout session for starting a new subscription. Returns checkout URL for payment.")
            .WithStandardResponses<StartSubscriptionResponse>();

        group.MapPost("/complete", CompleteCheckout)
            .WithName("CompleteCheckout")
            .WithApiMetadata(
                "Complete checkout session",
                "Verifies checkout session and creates subscription in database after successful payment.")
            .WithStandardResponses<SubscriptionDetailsResponse>();

        group.MapGet("/{customerId:guid}", GetCustomerSubscription)
            .WithName("GetCustomerSubscription")
            .WithApiMetadata(
                "Get customer subscription",
                "Retrieves the current subscription details for a customer, including plan information.")
            .WithStandardResponses<SubscriptionDetailsResponse>();

        group.MapPost("/{customerId:guid}/cancel", CancelSubscription)
            .WithName("CancelSubscription")
            .WithApiMetadata(
                "Cancel subscription",
                "Cancels a customer's subscription. Can cancel immediately or at period end (default).")
            .WithStandardResponses<CancelSubscriptionResponse>();

        group.MapPost("/{customerId:guid}/upgrade", UpgradeSubscription)
            .WithName("UpgradeSubscription")
            .WithApiMetadata(
                "Upgrade subscription",
                "Changes customer's subscription plan. Applies proration by default. Cannot downgrade during trial.")
            .WithStandardResponses<UpgradeSubscriptionResponse>();

        return group;
    }

    private static async Task<IResult> StartSubscription(
        [FromBody] StartSubscriptionRequest request,
        ISubscriptionApplicationService subscriptionService,
        CancellationToken ct)
    {
        var result = await subscriptionService.StartSubscriptionAsync(request, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> CompleteCheckout(
        [FromBody] CompleteCheckoutRequest request,
        ISubscriptionApplicationService subscriptionService,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.CheckoutSessionId))
            return Results.BadRequest("Checkout session id cannot be empty");

        var result = await subscriptionService.CompleteCheckoutAsync(
            request.CheckoutSessionId, ct);
        
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetCustomerSubscription(
        Guid customerId,
        ISubscriptionApplicationService subscriptionService,
        CancellationToken ct)
    {
        var result = await subscriptionService.GetCustomerSubscriptionAsync(customerId, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> CancelSubscription(
        Guid customerId,
        [FromBody] CancelSubscriptionRequest request,
        ISubscriptionApplicationService subscriptionService,
        CancellationToken ct)
    {
        var result = await subscriptionService.CancelSubscriptionAsync(customerId, request, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> UpgradeSubscription(
        Guid customerId,
        [FromBody] UpgradeSubscriptionRequest request,
        ISubscriptionApplicationService subscriptionService,
        CancellationToken ct)
    {
        var result = await subscriptionService.UpgradeSubscriptionAsync(customerId, request, ct);
        return result.ToHttpResult();
    }
}

/// <summary>
///     Request to complete checkout session
/// </summary>
public sealed class CompleteCheckoutRequest
{
    /// <summary>
    ///     Stripe checkout session ID (cs_xxx)
    /// </summary>
    public required string CheckoutSessionId { get; set; }
}