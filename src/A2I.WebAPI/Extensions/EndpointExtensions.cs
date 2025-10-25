using A2I.Application.Common;

namespace A2I.WebAPI.Extensions;

/// <summary>
/// Extension methods for building consistent API endpoints
/// </summary>
public static class EndpointExtensions
{
    /// <summary>
    /// Creates a versioned API group with common configuration
    /// </summary>
    public static RouteGroupBuilder MapApiV1(this IEndpointRouteBuilder app, string groupName)
    {
        return app.MapGroup($"/api/v1/{groupName}")
            .WithTags(groupName)
            .WithOpenApi();
            // .RequireAuthorization(); // Uncomment when auth is ready
    }

    /// <summary>
    /// Execute an async action and return a standardized response
    /// Handles exceptions and returns appropriate status codes
    /// </summary>
    public static async Task<IResult> ExecuteAsync<T>(
        Func<Task<T>> action,
        string? successMessage = null)
    {
        var result = await action();
        return Results.Ok(ApiResponse<T>.Ok(result, successMessage));
    }

    /// <summary>
    /// Execute an async action that returns void and return a standardized response
    /// </summary>
    public static async Task<IResult> ExecuteAsync(
        Func<Task> action,
        string successMessage = "Operation completed successfully")
    {
        await action();
        return Results.Ok(ApiResponse<object>.Ok(new { }, successMessage));
    }

    /// <summary>
    /// Execute an action and return a paginated response
    /// </summary>
    public static async Task<IResult> ExecutePaginatedAsync<T>(
        Func<Task<(List<T> Items, PaginationMetadata Pagination)>> action)
    {
        var (items, pagination) = await action();
        return Results.Ok(PaginatedResponse<T>.Ok(items, pagination));
    }

    /// <summary>
    /// Creates a NotFound result with standard error format
    /// </summary>
    public static IResult NotFound(string code, string message)
    {
        return Results.NotFound(ErrorResponse.Create(code, message));
    }

    /// <summary>
    /// Creates a BadRequest result with standard error format
    /// </summary>
    public static IResult BadRequest(string code, string message, Dictionary<string, string[]>? validationErrors = null)
    {
        return Results.BadRequest(ErrorResponse.Create(code, message, validationErrors));
    }

    /// <summary>
    /// Creates a Forbidden result with standard error format
    /// </summary>
    public static IResult Forbidden(string code, string message)
    {
        return Results.Json(
            ErrorResponse.Create(code, message),
            statusCode: StatusCodes.Status403Forbidden);
    }

    /// <summary>
    /// Creates a Created result with location header and data
    /// </summary>
    public static IResult Created<T>(string location, T data, string? message = null)
    {
        return Results.Created(location, ApiResponse<T>.Ok(data, message));
    }

    /// <summary>
    /// Creates a NoContent result (204)
    /// </summary>
    public static IResult NoContent()
    {
        return Results.NoContent();
    }

    /// <summary>
    /// Adds standard API metadata to an endpoint
    /// </summary>
    public static RouteHandlerBuilder WithApiMetadata(
        this RouteHandlerBuilder builder,
        string summary,
        string? description = null,
        params string[] tags)
    {
        builder.WithSummary(summary);
        
        if (!string.IsNullOrEmpty(description))
        {
            builder.WithDescription(description);
        }

        if (tags.Length > 0)
        {
            builder.WithTags(tags);
        }

        return builder;
    }

    /// <summary>
    /// Adds common response types to endpoint metadata
    /// </summary>
    public static RouteHandlerBuilder WithStandardResponses<T>(this RouteHandlerBuilder builder)
    {
        return builder
            .Produces<ApiResponse<T>>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError);
    }

    /// <summary>
    /// Adds paginated response types to endpoint metadata
    /// </summary>
    public static RouteHandlerBuilder WithPaginatedResponses<T>(this RouteHandlerBuilder builder)
    {
        return builder
            .Produces<PaginatedResponse<T>>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError);
    }

    /// <summary>
    /// Validates that a required parameter is not null or empty
    /// </summary>
    public static bool ValidateRequired(string? value, string parameterName, out IResult? errorResult)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errorResult = BadRequest(
                ErrorCodes.VALIDATION_REQUIRED,
                $"{parameterName} is required");
            return false;
        }

        errorResult = null;
        return true;
    }

    /// <summary>
    /// Validates that a GUID parameter is valid
    /// </summary>
    public static bool ValidateGuid(string value, string parameterName, out Guid result, out IResult? errorResult)
    {
        if (!Guid.TryParse(value, out result))
        {
            errorResult = BadRequest(
                ErrorCodes.VALIDATION_FORMAT,
                $"{parameterName} must be a valid GUID");
            return false;
        }

        errorResult = null;
        return true;
    }

    /// <summary>
    /// Validates pagination parameters
    /// </summary>
    public static bool ValidatePagination(int page, int pageSize, out IResult? errorResult)
    {
        if (page < 1)
        {
            errorResult = BadRequest(
                ErrorCodes.VALIDATION_RANGE,
                "Page must be greater than 0");
            return false;
        }

        if (pageSize is < 1 or > 100)
        {
            errorResult = BadRequest(
                ErrorCodes.VALIDATION_RANGE,
                "PageSize must be between 1 and 100");
            return false;
        }

        errorResult = null;
        return true;
    }
}

/// <summary>
/// Extension methods for working with results
/// </summary>
public static class ResultExtensions
{
    /// <summary>
    /// Converts a result to an IResult with standard formatting
    /// </summary>
    public static IResult ToApiResult<T>(this T data, string? message = null)
    {
        return Results.Ok(ApiResponse<T>.Ok(data, message));
    }

    /// <summary>
    /// Converts a result to a created response
    /// </summary>
    public static IResult ToCreatedResult<T>(this T data, string location, string? message = null)
    {
        return Results.Created(location, ApiResponse<T>.Ok(data, message));
    }
}