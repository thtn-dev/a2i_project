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
            .WithStandardResponses<Enable2FAResponse>();

        group.MapPost("/disable", Disable2FA)
            .RequireAuthorization()
            .WithApiMetadata(
                "Disable 2FA",
                "Disables two-factor authentication.")
            .WithStandardResponses();

        group.MapPost("/verify", Verify2FACode)
            .RequireAuthorization()
            .WithApiMetadata(
                "Verify 2FA code",
                "Verifies a two-factor authentication code from authenticator app.")
            .WithStandardResponses();

        group.MapPost("/recovery-codes", GenerateRecoveryCodes)
            .RequireAuthorization()
            .WithApiMetadata(
                "Generate recovery codes",
                "Generates new recovery codes for two-factor authentication.")
            .WithStandardResponses<GenerateRecoveryCodesResponse>();

        return group;
    }

    private static async Task<IResult> Enable2FA(
        ITwoFactorAuthService twoFactorService,
        ClaimsPrincipal user)
    {
        var userId = GetUserIdFromClaims(user);
        if (userId is null)
            return Results.Unauthorized();

        var result = await twoFactorService.Enable2FAAsync(userId.Value);
        return result.ToHttpResult();
    }

    private static async Task<IResult> Disable2FA(
        ITwoFactorAuthService twoFactorService,
        ClaimsPrincipal user)
    {
        var userId = GetUserIdFromClaims(user);
        if (userId is null)
            return Results.Unauthorized();

        var result = await twoFactorService.Disable2FAAsync(userId.Value);
        return result.ToHttpResult();
    }

    private static async Task<IResult> Verify2FACode(
        [FromBody] Verify2FARequest request,
        ITwoFactorAuthService twoFactorService,
        ClaimsPrincipal user)
    {
        var userId = GetUserIdFromClaims(user);
        if (userId is null)
            return Results.Unauthorized();

        var result = await twoFactorService.Verify2FACodeAsync(userId.Value, request.Code);
        return result.ToHttpResult();
    }

    private static async Task<IResult> GenerateRecoveryCodes(
        ITwoFactorAuthService twoFactorService,
        ClaimsPrincipal user)
    {
        var userId = GetUserIdFromClaims(user);
        if (userId is null)
            return Results.Unauthorized();

        var result = await twoFactorService.GenerateRecoveryCodesAsync(userId.Value);
        return result.ToHttpResult();
    }

    private static Guid? GetUserIdFromClaims(ClaimsPrincipal user)
    {
        var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(userIdClaim, out var userId) ? userId : null;
    }
}