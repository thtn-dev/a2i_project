using System.Diagnostics;
using A2I.Application.Common;
using FluentResults;
using Microsoft.AspNetCore.Mvc;

namespace A2I.WebAPI.Extensions;

public static class ResultPatternExtensions
{
    /// <summary>
    /// Gets the first success message or returns the default message if no successes exist
    /// </summary>
    public static string GetSuccessMessage(this ResultBase result, string defaultMessage)
    {
        return result.Successes.Count > 0 
            ? result.Successes[0].Message 
            : defaultMessage;
    }
    
    /// <summary>
    /// Gets the first success message or returns empty string if no successes exist
    /// </summary>
    private static string GetSuccessMessage(this ResultBase result)
    {
        return result.Successes.Count > 0 
            ? result.Successes[0].Message 
            : string.Empty;
    }
    
    /// <summary>
    /// Gets the first error message or returns the default message if no errors exist
    /// </summary>
    public static string GetErrorMessage(this ResultBase result, string defaultMessage = "An error occurred")
    {
        return result.Errors.Count > 0 
            ? result.Errors[0].Message 
            : defaultMessage;
    }
    
    public static IResult ToHttpResult<T>(this Result<T> result)
    {
        if (result.IsSuccess)
        {
            var firstSuccess = GetSuccessMessage(result, "completed");
            return Results.Ok(ApiResponse<T>.Ok(result.Value, firstSuccess));
        }
        return result.Errors[0].ToProblemDetails();
    }

    public static IResult ToHttpResult(this Result result)
    {
        return result.IsSuccess ? Results.NoContent() : result.Errors[0].ToProblemDetails();
    }

    private static IResult ToProblemDetails(this IError error)
    {
        var (statusCode, type, title) = error switch
        {
            UnexpectedError => (StatusCodes.Status500InternalServerError, "https://tools.ietf.org/html/rfc7231#section-6.6.1", "Internal Server Error"),
            NotFoundError => (StatusCodes.Status404NotFound, "https://tools.ietf.org/html/rfc7231#section-6.5.4", "Not Found"),
            ValidationError => (StatusCodes.Status400BadRequest, "https://tools.ietf.org/html/rfc7231#section-6.5.1", "Validation Error"),
            UnauthorizedError => (StatusCodes.Status401Unauthorized, "https://tools.ietf.org/html/rfc7235#section-3.1", "Unauthorized"),
            ForbiddenError => (StatusCodes.Status403Forbidden, "https://tools.ietf.org/html/rfc7231#section-6.5.3", "Forbidden"),
            ConflictError => (StatusCodes.Status409Conflict, "https://tools.ietf.org/html/rfc7231#section-6.5.8", "Conflict"),
            ExternalServiceError => (StatusCodes.Status502BadGateway, "https://tools.ietf.org/html/rfc7231#section-6.6.3", "External Service Error"),
            DatabaseError => (StatusCodes.Status500InternalServerError, "https://tools.ietf.org/html/rfc7231#section-6.6.1", "Database Error"),
            _ => (StatusCodes.Status500InternalServerError, "https://tools.ietf.org/html/rfc7231#section-6.6.1", "Internal Server Error")
        };

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Type = type,
            Title = title,
            Detail = error.Message,
            Extensions =
            {
                ["traceId"] = Activity.Current?.Id ?? Guid.NewGuid().ToString()
            }
        };
        
        if (error.Metadata.Count != 0)
        {
            problemDetails.Extensions["errors"] = error.Metadata;
        }

        return Results.Problem(problemDetails);
    }
}