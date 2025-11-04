using A2I.Application.Common;
using A2I.Infrastructure.Identity.Security;
using A2I.WebAPI.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace A2I.WebAPI.Endpoints.Auth;
public record LoginRequest(string Username, string Password);
public static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/login", Login)
            .WithApiMetadata(
                "User login",
                "Authenticates a user and returns a JWT token upon successful login.")
            .Produces<ApiResponse<object>>()
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError);
        return group;
    }
    
    private static async Task<IResult> Login(
        [FromBody] LoginRequest request,
        JwtService  jwtService)
    {
        if (request.Username != "admin" || request.Password != "password")
        {
            return Results.Unauthorized();
        }

        await Task.Delay(10);
        var token = jwtService.GenerateToken(
            userId: "123",
            username: request.Username,
            roles: ["Admin", "User"]
        );

        return Results.Ok(new { 
            access_token = token,
            token_type = "Bearer",
            expires_in = 3600
        });
    }
}