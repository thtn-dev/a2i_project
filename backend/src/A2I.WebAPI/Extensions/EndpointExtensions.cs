using A2I.Application.Common;
using FluentResults;
using Microsoft.AspNetCore.Mvc;

namespace A2I.WebAPI.Extensions;

/// <summary>
///     Extension methods for building consistent API endpoints
/// </summary>
public static class EndpointExtensions
{

    /// <summary>
    ///     Adds standard API metadata to an endpoint
    /// </summary>
    public static RouteHandlerBuilder WithApiMetadata(
        this RouteHandlerBuilder builder,
        string summary,
        string? description = null,
        params string[] tags)
    {
        builder.WithSummary(summary);

        if (!string.IsNullOrEmpty(description)) builder.WithDescription(description);

        if (tags.Length > 0) builder.WithTags(tags);

        return builder;
    }

    /// <summary>
    ///     Adds common response types to endpoint metadata.
    ///     Supports 200, 400, 404, and 500 responses
    /// </summary>
    public static RouteHandlerBuilder WithStandardResponses<T>(this RouteHandlerBuilder builder)
    {
        return builder
            .Produces<ApiResponse<T>>()
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status500InternalServerError);
    }
    
    public static RouteHandlerBuilder WithStandardResponses(this RouteHandlerBuilder builder)
    {
        return builder
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status500InternalServerError);
    }

    /// <summary>
    ///     Adds paginated response types to endpoint metadata
    /// </summary>
    public static RouteHandlerBuilder WithPaginatedResponses<T>(this RouteHandlerBuilder builder)
    {
        return builder
            .Produces<PaginatedResponse<T>>()
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status500InternalServerError);
    }



    /// <summary>
    ///     Validates pagination parameters
    /// </summary>
    public static bool ValidatePagination(int page, int pageSize, out IResult? errorResult)
    {
        if (page < 1)
        {
            errorResult = Results.BadRequest();
            return false;
        }

        if (pageSize is < 1 or > 100)
        {
            errorResult = Results.BadRequest();
            return false;
        }

        errorResult = null;
        return true;
    }
}
