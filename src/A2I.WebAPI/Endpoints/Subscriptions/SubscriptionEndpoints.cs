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
            .Produces<ApiResponse<StartSubscriptionResponse>>()
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError);

        group.MapPost("/complete", CompleteCheckout)
            .WithName("CompleteCheckout")
            .WithApiMetadata(
                "Complete checkout session",
                "Verifies checkout session and creates subscription in database after successful payment.")
            .Produces<ApiResponse<SubscriptionDetailsResponse>>()
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError);

        group.MapGet("/{customerId:guid}", GetCustomerSubscription)
            .WithName("GetCustomerSubscription")
            .WithApiMetadata(
                "Get customer subscription",
                "Retrieves the current subscription details for a customer, including plan information.")
            .Produces<ApiResponse<SubscriptionDetailsResponse>>()
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError);

        group.MapPost("/{customerId:guid}/cancel", CancelSubscription)
            .WithName("CancelSubscription")
            .WithApiMetadata(
                "Cancel subscription",
                "Cancels a customer's subscription. Can cancel immediately or at period end (default).")
            .Produces<ApiResponse<CancelSubscriptionResponse>>()
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError);

        group.MapPost("/{customerId:guid}/upgrade", UpgradeSubscription)
            .WithName("UpgradeSubscription")
            .WithApiMetadata(
                "Upgrade subscription",
                "Changes customer's subscription plan. Applies proration by default. Cannot downgrade during trial.")
            .Produces<ApiResponse<UpgradeSubscriptionResponse>>()
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError);

        return group;
    }

    // ==================== 1. START SUBSCRIPTION ====================

    private static async Task<IResult> StartSubscription(
        [FromBody] StartSubscriptionRequest request,
        ISubscriptionApplicationService subscriptionService,
        CancellationToken ct)
    {
        return await EndpointExtensions.ExecuteAsync(
            async () => await subscriptionService.StartSubscriptionAsync(request, ct),
            "Checkout session created successfully");
    }

    // ==================== 2. COMPLETE CHECKOUT ====================

    private static async Task<IResult> CompleteCheckout(
        [FromBody] CompleteCheckoutRequest request,
        ISubscriptionApplicationService subscriptionService,
        CancellationToken ct)
    {
        // Validate request
        if (string.IsNullOrWhiteSpace(request.CheckoutSessionId))
            return EndpointExtensions.BadRequest(
                ErrorCodes.VALIDATION_REQUIRED,
                "CheckoutSessionId is required");

        return await EndpointExtensions.ExecuteAsync(
            async () => await subscriptionService.CompleteCheckoutAsync(
                request.CheckoutSessionId, ct),
            "Subscription created successfully");
    }

    // ==================== 3. GET CUSTOMER SUBSCRIPTION ====================

    private static async Task<IResult> GetCustomerSubscription(
        Guid customerId,
        ISubscriptionApplicationService subscriptionService,
        CancellationToken ct)
    {
        var subscription = await subscriptionService.GetCustomerSubscriptionAsync(
            customerId, ct);

        if (subscription is null)
            return EndpointExtensions.NotFound(
                ErrorCodes.SUBSCRIPTION_NOT_FOUND,
                $"No subscription found for customer {customerId}");

        return Results.Ok(ApiResponse<SubscriptionDetailsResponse>.Ok(
            subscription,
            "Subscription retrieved successfully"));
    }

    // ==================== 4. CANCEL SUBSCRIPTION ====================

    private static async Task<IResult> CancelSubscription(
        Guid customerId,
        [FromBody] CancelSubscriptionRequest request,
        ISubscriptionApplicationService subscriptionService,
        CancellationToken ct)
    {
        return await EndpointExtensions.ExecuteAsync(
            async () => await subscriptionService.CancelSubscriptionAsync(
                customerId, request, ct),
            "Subscription cancelled successfully");
    }

    // ==================== 5. UPGRADE SUBSCRIPTION ====================

    private static async Task<IResult> UpgradeSubscription(
        Guid customerId,
        [FromBody] UpgradeSubscriptionRequest request,
        ISubscriptionApplicationService subscriptionService,
        CancellationToken ct)
    {
        return await EndpointExtensions.ExecuteAsync(
            async () => await subscriptionService.UpgradeSubscriptionAsync(
                customerId, request, ct),
            "Subscription upgraded successfully");
    }
}

// ==================== REQUEST MODELS ====================

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