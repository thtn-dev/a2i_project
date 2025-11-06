using System.Security.Claims;
using A2I.Application.Common;
using A2I.Infrastructure.Identity.Models;
using A2I.Infrastructure.Identity.Services;
using A2I.WebAPI.Extensions;
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

        var result = await accountService.ChangePasswordAsync(userId, request);

        if (result.IsFailed)
        {
            var errorMessage = result.GetErrorMessage();
            return Results.BadRequest(ErrorResponse.Create(errorMessage, errorMessage));
        }

        var successMessage = result.GetSuccessMessage("Password changed successfully");
        return Results.Ok(new ApiResponse(true, successMessage));
    }

    private static async Task<IResult> ForgotPassword(
        [FromBody] ForgotPasswordRequest request,
        [FromServices] IAccountService accountService)
    {
        var result = await accountService.ForgotPasswordAsync(request);

        var message = result.GetSuccessMessage("Password reset request processed");
        return Results.Ok(new ApiResponse(true, message));
    }

    private static async Task<IResult> ResetPassword(
        [FromBody] ResetPasswordRequest request,
        [FromServices] IAccountService accountService)
    {
        var result = await accountService.ResetPasswordAsync(request);

        if (result.IsFailed)
        {
            var errorMessage = result.GetErrorMessage();
            return Results.BadRequest(ErrorResponse.Create(errorMessage, errorMessage));
        }

        var successMessage = result.GetSuccessMessage("Password reset successfully");
        return Results.Ok(new ApiResponse(true, successMessage));
    }

    private static async Task<IResult> ConfirmEmail(
        [FromBody] ConfirmEmailRequest request,
        [FromServices] IAccountService accountService)
    {
        var result = await accountService.ConfirmEmailAsync(request);

        if (result.IsFailed)
        {
            var errorMessage = result.GetErrorMessage();
            return Results.BadRequest(ErrorResponse.Create(errorMessage, errorMessage));
        }

        var successMessage = result.GetSuccessMessage("Email confirmed successfully");
        return Results.Ok(new ApiResponse(true, successMessage));
    }

    private static async Task<IResult> ResendEmailConfirmation(
        [FromBody] ResendEmailConfirmationRequest request,
        [FromServices] IAccountService accountService)
    {
        var result = await accountService.ResendEmailConfirmationAsync(request);

        if (result.IsFailed)
        {
            var errorMessage = result.GetErrorMessage();
            return Results.BadRequest(ErrorResponse.Create(errorMessage, errorMessage));
        }

        var message = result.GetSuccessMessage("Confirmation email sent");
        return Results.Ok(new ApiResponse(true, message));
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

        var result = await accountService.UpdateProfileAsync(userId, request);

        if (result.IsFailed)
        {
            var errorMessage = result.GetErrorMessage();
            return Results.BadRequest(ErrorResponse.Create(errorMessage, errorMessage));
        }

        var successMessage = result.GetSuccessMessage("Profile updated successfully");
        return Results.Ok(ApiResponse<UserInfo>.Ok(result.Value, successMessage));
    }
}