using A2I.Application.Common;
using A2I.Infrastructure.Identity;
using A2I.Infrastructure.Identity.Models;
using A2I.Infrastructure.Identity.Services;
using A2I.WebAPI.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace A2I.WebAPI.Endpoints.Auth;

public static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder group)
    {
        // Authentication endpoints
        group.MapPost("/register", Register)
            .WithApiMetadata(
                "Register user",
                "Creates a new user account.")
            .Produces<ApiResponse>()
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest);

        group.MapPost("/login", Login)
            .WithApiMetadata(
                "User login",
                "Authenticates a user and returns JWT and refresh tokens.")
            .Produces<ApiResponse<LoginResponse>>()
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized);

        group.MapPost("/refresh-token", RefreshToken)
            .WithApiMetadata(
                "Refresh access token",
                "Generates a new access token using a valid refresh token.")
            .Produces<ApiResponse<LoginResponse>>()
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized);

        group.MapPost("/revoke-token", RevokeToken)
            .RequireAuthorization()
            .WithApiMetadata(
                "Revoke refresh token",
                "Revokes a refresh token to prevent its future use.")
            .Produces<ApiResponse>()
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized);

        group.MapPost("/logout", Logout)
            .RequireAuthorization()
            .WithApiMetadata(
                "User logout",
                "Revokes all active refresh tokens for the current user.")
            .Produces<ApiResponse>();

        return group;
    }

    private static async Task<IResult> Register(
        [FromBody] RegisterRequest request,
        [FromServices] IAuthService authService)
    {
        var (success, message, _) = await authService.RegisterAsync(request);

        if (!success)
        {
            return Results.BadRequest(ErrorResponse.Create(message, message));
        }

        return Results.Ok(new ApiResponse(success, message));
    }

    private static async Task<IResult> Login(
        [FromBody] LoginRequest request,
        [FromServices] IAuthService authService,
        HttpContext httpContext)
    {
        var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var (success, message, data) = await authService.LoginAsync(request, ipAddress);

        if (!success)
        {
            return Results.Unauthorized();
        }

        return Results.Ok(ApiResponse<LoginResponse>.Ok(data, message));
    }

    private static async Task<IResult> RefreshToken(
        [FromBody] RefreshTokenRequest request,
        [FromServices] IAuthService authService,
        HttpContext httpContext)
    {
        var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var (success, message, data) = await authService.RefreshTokenAsync(request.RefreshToken, ipAddress);

        if (!success)
        {
            return Results.Unauthorized();
        }
        return Results.Ok(ApiResponse<LoginResponse>.Ok(data, message));
    }

    private static async Task<IResult> RevokeToken(
        [FromBody] RefreshTokenRequest request,
        [FromServices] IAuthService authService,
        HttpContext httpContext)
    {
        var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var (success, message) = await authService.RevokeTokenAsync(request.RefreshToken, ipAddress);

        if (!success)
        {
            return Results.BadRequest(ErrorResponse.Create(message, message));
        }
        return Results.Ok(new ApiResponse(success, message));
    }

    private static async Task<IResult> Logout(
        [FromServices] IAuthService authService,
        [FromServices] AppIdentityDbContext dbContext,
        ClaimsPrincipal user,
        HttpContext httpContext)
    {
        var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userIdClaim == null || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Results.Unauthorized();
        }

        // Revoke all active refresh tokens for this user
        var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var activeTokens = dbContext.RefreshTokens
            .Where(t => t.UserId == userId && t.IsActive)
            .ToList();

        foreach (var token in activeTokens)
        {
            token.IsRevoked = true;
            token.RevokedAt = DateTime.UtcNow;
            token.RevokedByIp = ipAddress;
        }

        await dbContext.SaveChangesAsync();

        return Results.Ok(new ApiResponse(true, "Logged out successfully"));
    }
}
