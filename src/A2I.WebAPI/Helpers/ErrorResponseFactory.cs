using A2I.Application.Common;

namespace A2I.WebAPI.Helpers;

/// <summary>
/// Helper class for creating error responses with appropriate HTTP status codes
/// </summary>
public static class ErrorResponseFactory
{
    public static (ErrorResponse Response, int StatusCode) CreateNotFound(string code, string message)
    {
        return (ErrorResponse.Create(code, message), StatusCodes.Status404NotFound);
    }

    public static (ErrorResponse Response, int StatusCode) CreateBadRequest(string code, string message, Dictionary<string, string[]>? validationErrors = null)
    {
        return (ErrorResponse.Create(code, message, validationErrors), StatusCodes.Status400BadRequest);
    }

    public static (ErrorResponse Response, int StatusCode) CreateForbidden(string code, string message)
    {
        return (ErrorResponse.Create(code, message), StatusCodes.Status403Forbidden);
    }

    public static (ErrorResponse Response, int StatusCode) CreateInternalError(string message, string? traceId = null)
    {
        return (ErrorResponse.Create(ErrorCodes.INTERNAL_ERROR, message, traceId: traceId), StatusCodes.Status500InternalServerError);
    }

    public static (ErrorResponse Response, int StatusCode) CreateUnauthorized(string message)
    {
        return (ErrorResponse.Create(ErrorCodes.UNAUTHORIZED, message), StatusCodes.Status401Unauthorized);
    }
}