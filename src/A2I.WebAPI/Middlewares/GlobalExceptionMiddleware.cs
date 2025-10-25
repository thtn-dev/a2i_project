using System.Diagnostics;
using System.Net;
using System.Text.Json;
using A2I.Application.Common;
using A2I.Application.StripeAbstraction;

namespace A2I.WebAPI.Middlewares;

/// <summary>
/// Global exception handling middleware that catches all unhandled exceptions
/// and returns consistent error responses
/// </summary>
public class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    public GlobalExceptionMiddleware(
        RequestDelegate next,
        ILogger<GlobalExceptionMiddleware> logger,
        IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        // Get trace ID for tracking
        var traceId = Activity.Current?.Id ?? context.TraceIdentifier;
        
        // Log the exception with context
        _logger.LogError(
            exception,
            "Unhandled exception occurred. TraceId: {TraceId}, Path: {Path}, Method: {Method}",
            traceId,
            context.Request.Path,
            context.Request.Method);

        // Determine status code and error response based on exception type
        var (statusCode, errorResponse) = exception switch
        {
            // Business exceptions
            BusinessException businessEx => HandleBusinessException(businessEx, traceId),
            
            // Stripe exceptions
            StripeNotFoundException notFoundEx => HandleStripeNotFoundException(notFoundEx, traceId),
            StripeServiceException stripeEx => HandleStripeException(stripeEx, traceId),
            
            // Validation exceptions (from FluentValidation if you add it later)
            ArgumentException argEx => HandleArgumentException(argEx, traceId),
            
            // Unknown exceptions
            _ => HandleUnknownException(exception, traceId)
        };

        // Set response details
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = statusCode;

        // Serialize and write response
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = _environment.IsDevelopment()
        };

        var json = JsonSerializer.Serialize(errorResponse, options);
        await context.Response.WriteAsync(json);
    }

    private (int StatusCode, ErrorResponse Response) HandleBusinessException(
        BusinessException exception, 
        string traceId)
    {
        // Business exceptions are expected and should return 400 Bad Request
        // unless they indicate a resource not found
        var isNotFound = exception.Message.Contains("not found", StringComparison.OrdinalIgnoreCase);
        
        var statusCode = isNotFound 
            ? StatusCodes.Status404NotFound 
            : StatusCodes.Status400BadRequest;

        var errorCode = isNotFound 
            ? ErrorCodes.NOT_FOUND 
            : ErrorCodes.BAD_REQUEST;

        return (statusCode, ErrorResponse.Create(
            code: errorCode,
            message: exception.Message,
            traceId: traceId
        ));
    }

    private (int StatusCode, ErrorResponse Response) HandleStripeNotFoundException(
        StripeNotFoundException exception,
        string traceId)
    {
        return (StatusCodes.Status404NotFound, ErrorResponse.Create(
            code: ErrorCodes.STRIPE_RESOURCE_NOT_FOUND,
            message: exception.Message,
            traceId: traceId
        ));
    }

    private (int StatusCode, ErrorResponse Response) HandleStripeException(
        StripeServiceException exception,
        string traceId)
    {
        // Map Stripe HTTP status codes to appropriate responses
        var statusCode = exception.StatusCode switch
        {
            HttpStatusCode.BadRequest => StatusCodes.Status400BadRequest,
            HttpStatusCode.Unauthorized => StatusCodes.Status401Unauthorized,
            HttpStatusCode.PaymentRequired => StatusCodes.Status402PaymentRequired,
            HttpStatusCode.NotFound => StatusCodes.Status404NotFound,
            HttpStatusCode.TooManyRequests => StatusCodes.Status429TooManyRequests,
            _ => StatusCodes.Status502BadGateway // Stripe service unavailable
        };

        var errorCode = exception.StatusCode switch
        {
            HttpStatusCode.PaymentRequired => ErrorCodes.STRIPE_PAYMENT_FAILED,
            HttpStatusCode.NotFound => ErrorCodes.STRIPE_RESOURCE_NOT_FOUND,
            _ => ErrorCodes.STRIPE_API_ERROR
        };

        return (statusCode, ErrorResponse.Create(
            code: errorCode,
            message: $"Stripe error: {exception.Message}",
            traceId: traceId
        ));
    }

    private (int StatusCode, ErrorResponse Response) HandleArgumentException(
        ArgumentException exception,
        string traceId)
    {
        return (StatusCodes.Status400BadRequest, ErrorResponse.Create(
            code: ErrorCodes.VALIDATION_FAILED,
            message: exception.Message,
            traceId: traceId
        ));
    }

    private (int StatusCode, ErrorResponse Response) HandleUnknownException(
        Exception exception,
        string traceId)
    {
        // For unknown exceptions, return generic error in production
        // but include details in development
        var message = _environment.IsDevelopment()
            ? $"Internal server error: {exception.Message}"
            : "An unexpected error occurred. Please contact support if the problem persists.";

        return (StatusCodes.Status500InternalServerError, ErrorResponse.Create(
            code: ErrorCodes.INTERNAL_ERROR,
            message: message,
            traceId: traceId
        ));
    }
}

/// <summary>
/// Extension method for registering the middleware
/// </summary>
public static class GlobalExceptionMiddlewareExtensions
{
    public static IApplicationBuilder UseGlobalExceptionHandler(this IApplicationBuilder app)
    {
        return app.UseMiddleware<GlobalExceptionMiddleware>();
    }
}