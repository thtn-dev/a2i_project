using A2I.Application.Common;
using A2I.Infrastructure.Identity.Models;
using A2I.Infrastructure.Identity.Services;
using A2I.WebAPI.Extensions;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace A2I.WebAPI.Endpoints.Auth;

public static class TwoFactorEndpoints
{
    public static RouteGroupBuilder MapTwoFactorEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/enable", Enable2FA)
            .RequireAuthorization()
            .WithApiMetadata(
                "Enable 2FA",
                "Enables two-factor authentication and returns QR code data.")
            .Produces<ApiResponse<Enable2FAResponse>>()
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest);

        group.MapPost("/disable", Disable2FA)
            .RequireAuthorization()
            .WithApiMetadata(
                "Disable 2FA",
                "Disables two-factor authentication.")
            .Produces<ApiResponse>()
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest);

        group.MapPost("/verify", Verify2FACode)
            .RequireAuthorization()
            .WithApiMetadata(
                "Verify 2FA code",
                "Verifies a two-factor authentication code from authenticator app.")
            .Produces<ApiResponse>()
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest);

        group.MapPost("/recovery-codes", GenerateRecoveryCodes)
            .RequireAuthorization()
            .WithApiMetadata(
                "Generate recovery codes",
                "Generates new recovery codes for two-factor authentication.")
            .Produces<ApiResponse<GenerateRecoveryCodesResponse>>()
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest);

        return group;
    }

    private static async Task<IResult> Enable2FA(
        [FromServices] ITwoFactorAuthService twoFactorService,
        ClaimsPrincipal user)
    {
        var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userIdClaim == null || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Results.Unauthorized();
        }

        var (success, message, data) = await twoFactorService.Enable2FAAsync(userId);

        if (!success)
        {
            return Results.BadRequest(new ErrorResponse(message));
        }

        return Results.Ok(new ApiResponse<Enable2FAResponse>(success, message, data));
    }

    private static async Task<IResult> Disable2FA(
        [FromServices] ITwoFactorAuthService twoFactorService,
        ClaimsPrincipal user)
    {
        var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userIdClaim == null || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Results.Unauthorized();
        }

        var (success, message) = await twoFactorService.Disable2FAAsync(userId);

        if (!success)
        {
            return Results.BadRequest(new ErrorResponse(message));
        }

        return Results.Ok(new ApiResponse(success, message));
    }

    private static async Task<IResult> Verify2FACode(
        [FromBody] Verify2FARequest request,
        [FromServices] ITwoFactorAuthService twoFactorService,
        ClaimsPrincipal user)
    {
        var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userIdClaim == null || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Results.Unauthorized();
        }

        var (success, message) = await twoFactorService.Verify2FACodeAsync(userId, request.Code);

        if (!success)
        {
            return Results.BadRequest(new ErrorResponse(message));
        }

        return Results.Ok(new ApiResponse(success, message));
    }

    private static async Task<IResult> GenerateRecoveryCodes(
        [FromServices] ITwoFactorAuthService twoFactorService,
        ClaimsPrincipal user)
    {
        var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userIdClaim == null || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Results.Unauthorized();
        }

        var (success, message, data) = await twoFactorService.GenerateRecoveryCodesAsync(userId);

        if (!success)
        {
            return Results.BadRequest(new ErrorResponse(message));
        }

        return Results.Ok(new ApiResponse<GenerateRecoveryCodesResponse>(success, message, data));
    }
}
