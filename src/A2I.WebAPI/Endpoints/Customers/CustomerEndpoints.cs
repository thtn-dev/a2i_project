using A2I.Application.Common;
using A2I.Application.Customers;
using A2I.WebAPI.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace A2I.WebAPI.Endpoints.Customers;

/// <summary>
/// Customer management endpoints
/// </summary>
public static class CustomerEndpoints
{
    public static RouteGroupBuilder MapCustomerEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/", CreateOrUpdateCustomer)
            .WithName("CreateOrUpdateCustomer")
            .WithApiMetadata(
                "Create or update customer",
                "Creates a new customer in both database and Stripe, or updates existing customer information.")
            .Produces<ApiResponse<CustomerDetailsResponse>>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError);

        group.MapGet("/{customerId:guid}", GetCustomerDetails)
            .WithName("GetCustomerDetails")
            .WithApiMetadata(
                "Get customer details",
                "Retrieves complete customer information including active subscription, recent invoices, and payment methods.")
            .Produces<ApiResponse<CustomerDetailsResponse>>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError);

        group.MapPut("/{customerId:guid}/payment-method", UpdatePaymentMethod)
            .WithName("UpdatePaymentMethod")
            .WithApiMetadata(
                "Update payment method",
                "Attaches a new payment method to the customer and optionally sets it as default for future invoices.")
            .Produces<ApiResponse<UpdatePaymentMethodResponse>>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError);

        group.MapPost("/{customerId:guid}/portal", GetCustomerPortalUrl)
            .WithName("GetCustomerPortalUrl")
            .WithApiMetadata(
                "Get customer portal URL",
                "Creates a Stripe Customer Portal session for self-service management of subscription, invoices, and payment methods.")
            .Produces<ApiResponse<CustomerPortalResponse>>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError);

        return group;
    }

    // ==================== 1. CREATE OR UPDATE CUSTOMER ====================

    private static async Task<IResult> CreateOrUpdateCustomer(
        [FromBody] CreateOrUpdateCustomerRequest request,
        ICustomerApplicationService customerService,
        CancellationToken ct)
    {
        return await EndpointExtensions.ExecuteAsync(
            async () => await customerService.CreateOrUpdateCustomerAsync(request, ct),
            "Customer saved successfully");
    }

    // ==================== 2. GET CUSTOMER DETAILS ====================

    private static async Task<IResult> GetCustomerDetails(
        Guid customerId,
        ICustomerApplicationService customerService,
        CancellationToken ct)
    {
        return await EndpointExtensions.ExecuteAsync(
            async () => await customerService.GetCustomerDetailsAsync(customerId, ct),
            "Customer details retrieved successfully");
    }

    // ==================== 3. UPDATE PAYMENT METHOD ====================

    private static async Task<IResult> UpdatePaymentMethod(
        Guid customerId,
        [FromBody] UpdatePaymentMethodRequest request,
        ICustomerApplicationService customerService,
        CancellationToken ct)
    {
        return await EndpointExtensions.ExecuteAsync(
            async () => await customerService.UpdatePaymentMethodAsync(customerId, request, ct),
            "Payment method updated successfully");
    }

    // ==================== 4. GET CUSTOMER PORTAL URL ====================

    private static async Task<IResult> GetCustomerPortalUrl(
        Guid customerId,
        [FromBody] GetPortalUrlRequest request,
        ICustomerApplicationService customerService,
        CancellationToken ct)
    {
        // Validate returnUrl
        if (string.IsNullOrWhiteSpace(request.ReturnUrl))
        {
            return EndpointExtensions.BadRequest(
                ErrorCodes.VALIDATION_REQUIRED,
                "ReturnUrl is required");
        }

        // Validate URL format
        if (!Uri.TryCreate(request.ReturnUrl, UriKind.Absolute, out _))
        {
            return EndpointExtensions.BadRequest(
                ErrorCodes.VALIDATION_FORMAT,
                "ReturnUrl must be a valid absolute URL");
        }

        return await EndpointExtensions.ExecuteAsync(
            async () => await customerService.GetCustomerPortalUrlAsync(
                customerId, request.ReturnUrl, ct),
            "Portal URL created successfully");
    }
}

// ==================== REQUEST MODELS ====================

/// <summary>
/// Request to get customer portal URL
/// </summary>
public sealed class GetPortalUrlRequest
{
    /// <summary>
    /// URL to redirect customer after they finish managing their subscription
    /// </summary>
    public required string ReturnUrl { get; set; }
}