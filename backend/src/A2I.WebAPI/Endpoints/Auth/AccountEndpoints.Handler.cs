using System.Security.Claims;
using A2I.Application.Common;
using A2I.Infrastructure.Identity.Models;
using A2I.Infrastructure.Identity.Services;
using Microsoft.AspNetCore.Mvc;

namespace A2I.WebAPI.Endpoints.Auth;

public static partial class AccountEndpoints
{
    private static async Task<IResult> ChangePassword(
        [FromBody] ChangePasswordRequest request,
        [FromServices] IAccountService accountService,
        ClaimsPrincipal user)
    {
        var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userIdClaim == null || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Results.Unauthorized();
        }

        var (success, message) = await accountService.ChangePasswordAsync(userId, request);

        if (!success)
        {
            return Results.BadRequest(ErrorResponse.Create(message, message));
        }

        return Results.Ok(new ApiResponse(success, message));
    }

    private static async Task<IResult> ForgotPassword(
        [FromBody] ForgotPasswordRequest request,
        [FromServices] IAccountService accountService)
    {
        var (success, message) = await accountService.ForgotPasswordAsync(request);

        return Results.Ok(new ApiResponse(success, message));
    }

    private static async Task<IResult> ResetPassword(
        [FromBody] ResetPasswordRequest request,
        [FromServices] IAccountService accountService)
    {
        var (success, message) = await accountService.ResetPasswordAsync(request);

        if (!success)
        {
            return Results.BadRequest(ErrorResponse.Create(message, message));
        }

        return Results.Ok(new ApiResponse(success, message));
    }

    private static async Task<IResult> ConfirmEmail(
        [FromBody] ConfirmEmailRequest request,
        [FromServices] IAccountService accountService)
    {
        var (success, message) = await accountService.ConfirmEmailAsync(request);

        if (!success)
        {
            return Results.BadRequest(ErrorResponse.Create(message, message));
        }

        return Results.Ok(new ApiResponse(success, message));
    }

    private static async Task<IResult> ResendEmailConfirmation(
        [FromBody] ResendEmailConfirmationRequest request,
        [FromServices] IAccountService accountService)
    {
        var (success, message) = await accountService.ResendEmailConfirmationAsync(request);

        return Results.Ok(new ApiResponse(success, message));
    }

    private static async Task<IResult> GetCurrentUser(
        [FromServices] IAuthService authService,
        ClaimsPrincipal user)
    {
        var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userIdClaim == null || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Results.Unauthorized();
        }

        var userInfo = await authService.GetUserInfoAsync(userId);

        if (userInfo == null)
        {
            return Results.NotFound(ErrorResponse.Create("", "User not found"));
        }

        return Results.Ok(ApiResponse<UserInfo>.Ok(userInfo, "User retrieved successfully"));
    }

    private static async Task<IResult> UpdateProfile(
        [FromBody] UpdateProfileRequest request,
        [FromServices] IAccountService accountService,
        ClaimsPrincipal user)
    {
        var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userIdClaim == null || !Guid.TryParse(userIdClaim, out var userId))
        {
            return Results.Unauthorized();
        }

        var (success, message, data) = await accountService.UpdateProfileAsync(userId, request);

        if (!success)
        {
            return Results.BadRequest(ErrorResponse.Create("", "User not found"));
        }

        return Results.Ok(ApiResponse<UserInfo>.Ok(data, message));
    }
}