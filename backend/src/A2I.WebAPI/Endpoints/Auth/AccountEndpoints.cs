using A2I.Application.Common;
using A2I.Infrastructure.Identity.Models;
using A2I.WebAPI.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace A2I.WebAPI.Endpoints.Auth;

public static partial class AccountEndpoints
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
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest);

        group.MapPost("/forgot-password", ForgotPassword)
            .WithApiMetadata(
                "Forgot password",
                "Sends a password reset link to the user's email.")
            .Produces<ApiResponse>()
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest);

        group.MapPost("/reset-password", ResetPassword)
            .WithApiMetadata(
                "Reset password",
                "Resets the user's password using a valid reset token.")
            .Produces<ApiResponse>()
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest);

        // Email Confirmation
        group.MapPost("/confirm-email", ConfirmEmail)
            .WithApiMetadata(
                "Confirm email",
                "Confirms the user's email address using a confirmation token.")
            .Produces<ApiResponse>()
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest);

        group.MapPost("/resend-email-confirmation", ResendEmailConfirmation)
            .WithApiMetadata(
                "Resend email confirmation",
                "Resends the email confirmation link.")
            .Produces<ApiResponse>()
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest);

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
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest);

        return group;
    }
}
