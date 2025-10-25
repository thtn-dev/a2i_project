using System.Net;
using A2I.Application.Common;
using A2I.Application.StripeAbstraction;
using A2I.WebAPI.Extensions;

namespace A2I.WebAPI.Endpoints.Test;

/// <summary>
/// Test endpoints to verify foundation components
/// REMOVE IN PRODUCTION!
/// </summary>
public static class TestEndpoints
{
    public static RouteGroupBuilder MapTestEndpoints(this RouteGroupBuilder group)
    {
        // Test successful response
        group.MapGet("/success", TestSuccess)
            .WithName("TestSuccess")
            .WithApiMetadata("Test successful response", "Returns a successful API response")
            .WithStandardResponses<TestData>();

        // Test business exception
        group.MapGet("/business-error", TestBusinessError)
            .WithName("TestBusinessError")
            .WithApiMetadata("Test business exception", "Throws a business exception to test error handling")
            .WithStandardResponses<TestData>();

        // Test validation error
        group.MapGet("/validation-error", TestValidationError)
            .WithName("TestValidationError")
            .WithApiMetadata("Test validation error", "Returns a validation error to test error formatting")
            .WithStandardResponses<TestData>();

        // Test not found error
        group.MapGet("/not-found", TestNotFound)
            .WithName("TestNotFound")
            .WithApiMetadata("Test not found response", "Returns a 404 not found response")
            .WithStandardResponses<TestData>();

        // Test stripe error
        group.MapGet("/stripe-error", TestStripeError)
            .WithName("TestStripeError")
            .WithApiMetadata("Test Stripe exception", "Throws a Stripe exception to test error handling")
            .WithStandardResponses<TestData>();

        // Test unhandled exception
        group.MapGet("/unhandled-error", TestUnhandledException)
            .WithName("TestUnhandledException")
            .WithApiMetadata("Test unhandled exception", "Throws an unhandled exception to test global error handler")
            .WithStandardResponses<TestData>();

        // Test paginated response
        group.MapGet("/paginated", TestPaginated)
            .WithName("TestPaginated")
            .WithApiMetadata("Test paginated response", "Returns a paginated response")
            .WithPaginatedResponses<TestData>();

        // Test async execution helper
        group.MapGet("/async-helper", TestAsyncHelper)
            .WithName("TestAsyncHelper")
            .WithApiMetadata("Test async execution helper", "Tests the ExecuteAsync helper method")
            .WithStandardResponses<TestData>();

        return group;
    }

    private static IResult TestSuccess()
    {
        return Results.Ok(ApiResponse<TestData>.Ok(
            new TestData
            {
                Id = Guid.NewGuid(),
                Name = "Test Item",
                Description = "This is a successful response",
                CreatedAt = DateTime.UtcNow
            },
            "Operation completed successfully"));
    }

    private static IResult TestBusinessError()
    {
        throw new BusinessException("This is a test business exception");
    }

    private static IResult TestValidationError()
    {
        return EndpointExtensions.BadRequest(
            ErrorCodes.VALIDATION_FAILED,
            "Validation failed",
            new Dictionary<string, string[]>
            {
                ["email"] = new[] { "Email is required", "Email format is invalid" },
                ["password"] = new[] { "Password must be at least 8 characters" }
            });
    }

    private static IResult TestNotFound()
    {
        return EndpointExtensions.NotFound(
            ErrorCodes.CUSTOMER_NOT_FOUND,
            "Customer with ID '12345' not found");
    }

    private static IResult TestStripeError()
    {
        // Simulate a Stripe exception
        var stripeEx = new Stripe.StripeException("Test Stripe error")
        {
            HttpStatusCode = HttpStatusCode.BadRequest
        };

        throw StripeErrorMapper.Wrap(stripeEx);
    }

    private static IResult TestUnhandledException()
    {
        throw new InvalidOperationException("This is an unhandled exception for testing");
    }

    private static IResult TestPaginated()
    {
        var items = Enumerable.Range(1, 10)
            .Select(i => new TestData
            {
                Id = Guid.NewGuid(),
                Name = $"Item {i}",
                Description = $"Description for item {i}",
                CreatedAt = DateTime.UtcNow.AddDays(-i)
            })
            .ToList();

        var pagination = new PaginationMetadata
        {
            CurrentPage = 1,
            PageSize = 10,
            TotalItems = 100,
            TotalPages = 10,
            HasPreviousPage = false,
            HasNextPage = true
        };

        return Results.Ok(PaginatedResponse<TestData>.Ok(items, pagination));
    }

    private static async Task<IResult> TestAsyncHelper()
    {
        return await EndpointExtensions.ExecuteAsync(
            async () =>
            {
                // Simulate async work
                await Task.Delay(100);
                
                return new TestData
                {
                    Id = Guid.NewGuid(),
                    Name = "Async Test",
                    Description = "This was returned using ExecuteAsync helper",
                    CreatedAt = DateTime.UtcNow
                };
            },
            "Async operation completed successfully");
    }
}

// ==================== TEST MODELS ====================

public class TestData
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}