using A2I.Application.Common;
using A2I.Application.Customers;
using A2I.WebAPI.Extensions;
using FluentResults;
using Microsoft.AspNetCore.Mvc;

namespace A2I.WebAPI.Endpoints.Customers;

/// <summary>
///     Customer management endpoints
/// </summary>
public static class CustomerEndpoints
{
    public static RouteGroupBuilder MapCustomerEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/", CreateOrUpdateCustomer)
            .WithApiMetadata(
                "Create or update customer",
                "Creates a new customer in both database and Stripe, or updates existing customer information.")
            .WithStandardResponses<CustomerDetailsResponse>();

        group.MapGet("/{customerId:guid}", GetCustomerDetails)
            .WithName("GetCustomerDetails")
            .WithApiMetadata(
                "Get customer details",
                "Retrieves complete customer information including active subscription, recent invoices, and payment methods.")
            .WithStandardResponses<CustomerDetailsResponse>();

        group.MapPut("/{customerId:guid}/payment-method", UpdatePaymentMethod)
            .WithName("UpdatePaymentMethod")
            .WithApiMetadata(
                "Update payment method",
                "Attaches a new payment method to the customer and optionally sets it as default for future invoices.")
            .WithStandardResponses<UpdatePaymentMethodResponse>();

        group.MapPost("/{customerId:guid}/portal", GetCustomerPortalUrl)
            .WithName("GetCustomerPortalUrl")
            .WithApiMetadata(
                "Get customer portal URL",
                "Creates a Stripe Customer Portal session for self-service management of subscription, invoices, and payment methods.")
            .WithStandardResponses<CustomerPortalResponse>();

        return group;
    }

    private static async Task<IResult> CreateOrUpdateCustomer(
        [FromBody] CreateOrUpdateCustomerRequest request,
        ICustomerApplicationService customerService,
        CancellationToken ct)
    {
        var result = await customerService.CreateOrUpdateCustomerAsync(request, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetCustomerDetails(
        Guid customerId,
        ICustomerApplicationService customerService,
        CancellationToken ct)
    {
        var result = await customerService.GetCustomerDetailsAsync(customerId, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> UpdatePaymentMethod(
        Guid customerId,
        [FromBody] UpdatePaymentMethodRequest request,
        ICustomerApplicationService customerService,
        CancellationToken ct)
    {
        var result = await customerService.UpdatePaymentMethodAsync(customerId, request, ct);
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetCustomerPortalUrl(
        Guid customerId,
        [FromBody] GetPortalUrlRequest request,
        ICustomerApplicationService customerService,
        CancellationToken ct)
    {
        // Validate returnUrl
        if (string.IsNullOrWhiteSpace(request.ReturnUrl))
            return Results.BadRequest();

        // Validate URL format
        if (!Uri.TryCreate(request.ReturnUrl, UriKind.Absolute, out _))
            return Results.BadRequest( "ReturnUrl must be a valid absolute URL");
        
        var result = await customerService.GetCustomerPortalUrlAsync(
            customerId, request.ReturnUrl, ct);
        
        return result.ToHttpResult();
    }
}

// ==================== REQUEST MODELS ====================

/// <summary>
///     Request to get customer portal URL
/// </summary>
public sealed class GetPortalUrlRequest
{
    /// <summary>
    ///     URL to redirect customer after they finish managing their subscription
    /// </summary>
    public string ReturnUrl { get; set; } = string.Empty;
}