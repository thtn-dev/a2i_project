using A2I.WebAPI.Endpoints.Auth;
using A2I.WebAPI.Endpoints.Customers;
using A2I.WebAPI.Endpoints.Invoices;
using A2I.WebAPI.Endpoints.Subscriptions;
using A2I.WebAPI.Endpoints.System;

namespace A2I.WebAPI.Endpoints;

public static class RegisterV1EndpointExtensions
{
    public static IEndpointRouteBuilder MapV1Endpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGroup("/health")
            .WithTags("System")
            .MapHealthEndpoints();
        
        endpoints.MapGroup("/subscriptions")
            .WithTags("Subscriptions")
            .MapSubscriptionEndpoints();

        endpoints.MapGroup("/customers")
            .WithTags("Customers")
            .MapCustomerEndpoints();

        endpoints.MapGroup("/invoices")
            .WithTags("Invoices")
            .MapInvoiceEndpoints();
        
        endpoints.MapGroup("/auth")
            .WithTags("Auth")
            .MapAuthEndpoints();

        endpoints.MapGroup("/account")
            .WithTags("Account")
            .MapAccountEndpoints();

        endpoints.MapGroup("/2fa")
            .WithTags("Two-Factor Authentication")
            .MapTwoFactorEndpoints();
        
        return endpoints;
    }
}