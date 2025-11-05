using A2I.Application.Common;
using A2I.Infrastructure.Identity.Models;
using A2I.Infrastructure.Identity.Services;
using A2I.WebAPI.Extensions;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace A2I.WebAPI.Endpoints.Auth;

public static class AccountEndpoints
{
    public static RouteGroupBuilder MapAccountEndpoints(this RouteGroupBuilder group)
    {
        // Password Management
        group.MapPost("/change-password", ChangePassword)
            .RequireAuthorization()
            .WithApiMetadata(
                "Change password",
                "Changes the password for the authenticated user.")
            .Produces<ApiResponse>()
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest);

        group.MapPost("/forgot-password", ForgotPassword)
            .WithApiMetadata(
                "Forgot password",
                "Sends a password reset link to the user's email.")
            .Produces<ApiResponse>()
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest);

        group.MapPost("/reset-password", ResetPassword)
            .WithApiMetadata(
                "Reset password",
                "Resets the user's password using a valid reset token.")
            .Produces<ApiResponse>()
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest);

        // Email Confirmation
        group.MapPost("/confirm-email", ConfirmEmail)
            .WithApiMetadata(
                "Confirm email",
                "Confirms the user's email address using a confirmation token.")
            .Produces<ApiResponse>()
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest);

        group.MapPost("/resend-email-confirmation", ResendEmailConfirmation)
            .WithApiMetadata(
                "Resend email confirmation",
                "Resends the email confirmation link.")
            .Produces<ApiResponse>()
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest);

        // Profile Management
        group.MapGet("/me", GetCurrentUser)
            .RequireAuthorization()
            .WithApiMetadata(
                "Get current user",
                "Gets information about the authenticated user.")
            .Produces<ApiResponse<UserInfo>>();

        group.MapPut("/me", UpdateProfile)
            .RequireAuthorization()
            .WithApiMetadata(
                "Update profile",
                "Updates the authenticated user's profile information.")
            .Produces<ApiResponse<UserInfo>>()
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest);

        return group;
    }

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
