using System.Diagnostics;
using System.Net;
using System.Text.Json;
using A2I.Application.Common;
using A2I.Application.StripeAbstraction;
using Microsoft.AspNetCore.Mvc;

namespace A2I.WebAPI.Middlewares;

/// <summary>
///     Global exception handling middleware that catches all unhandled exceptions
///     and returns consistent error responses
/// </summary>
public class GlobalExceptionMiddleware(
    RequestDelegate next,
    ILogger<GlobalExceptionMiddleware> logger,
    IHostEnvironment environment)
{
    private readonly ILogger<GlobalExceptionMiddleware> _logger = logger;

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            context.Response.ContentType = "application/json";
            context.Response.StatusCode = 500;
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = environment.IsDevelopment()
            };
            var traceId = Activity.Current?.Id ?? context.TraceIdentifier;
            var problemDetails = new ProblemDetails
            {
                Status = (int)HttpStatusCode.InternalServerError,
                Title = "An unexpected error occurred.",
                Detail = environment.IsDevelopment() ? ex.ToString() : "Please contact support with the trace ID.",
                Instance = traceId
            };
            var json = JsonSerializer.Serialize(problemDetails, options);
            await context.Response.WriteAsync(json);
        }
    }
}

/// <summary>
///     Extension method for registering the middleware
/// </summary>
public static class GlobalExceptionMiddlewareExtensions
{
    public static void UseGlobalExceptionHandler(this IApplicationBuilder app)
    {
        app.UseMiddleware<GlobalExceptionMiddleware>();
    }
}