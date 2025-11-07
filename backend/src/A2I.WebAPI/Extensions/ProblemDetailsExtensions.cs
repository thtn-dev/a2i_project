using FluentResults;
using Microsoft.AspNetCore.Mvc;

namespace A2I.WebAPI.Extensions;

public static class ProblemDetailsExtensions
{
    public static ProblemDetails ToProblemDetails(this Exception ex, int statusCode = StatusCodes.Status500InternalServerError)
    {
        return new ProblemDetails
        {
            Title = "An error occurred while processing your request.",
            Detail = ex.Message,
            Status = statusCode
        };
    }
    
}