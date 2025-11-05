using A2I.Infrastructure.Identity.Entities;
using A2I.Infrastructure.Identity.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using System.Text;

namespace A2I.Infrastructure.Identity.Services;

public interface IAccountService
{
    Task<(bool Success, string Message)> ChangePasswordAsync(Guid userId, ChangePasswordRequest request);
    Task<(bool Success, string Message)> ForgotPasswordAsync(ForgotPasswordRequest request);
    Task<(bool Success, string Message)> ResetPasswordAsync(ResetPasswordRequest request);
    Task<(bool Success, string Message)> ConfirmEmailAsync(ConfirmEmailRequest request);
    Task<(bool Success, string Message)> ResendEmailConfirmationAsync(ResendEmailConfirmationRequest request);
    Task<(bool Success, string Message, UserInfo? Data)> UpdateProfileAsync(Guid userId, UpdateProfileRequest request);
}

public class AccountService : IAccountService
{
    private readonly UserManager<ApplicationUser> _userManager;

    public AccountService(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<(bool Success, string Message)> ChangePasswordAsync(Guid userId, ChangePasswordRequest request)
    {
        if (request.NewPassword != request.ConfirmNewPassword)
        {
            return (false, "New passwords do not match");
        }

        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null)
        {
            return (false, "User not found");
        }

        var result = await _userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);

        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            return (false, $"Password change failed: {errors}");
        }

        return (true, "Password changed successfully");
    }

    public async Task<(bool Success, string Message)> ForgotPasswordAsync(ForgotPasswordRequest request)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);

        // Don't reveal if user exists or not for security reasons
        if (user == null)
        {
            return (true, "If the email exists, a password reset link has been sent.");
        }

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);

        // Encode token for URL
        var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

        // TODO: Send email with reset link
        // Example link: https://yourdomain.com/reset-password?email={email}&token={encodedToken}

        return (true, "If the email exists, a password reset link has been sent.");
    }

    public async Task<(bool Success, string Message)> ResetPasswordAsync(ResetPasswordRequest request)
    {
        if (request.NewPassword != request.ConfirmNewPassword)
        {
            return (false, "Passwords do not match");
        }

        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user == null)
        {
            return (false, "Invalid request");
        }

        // Decode token from URL
        var decodedToken = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(request.Token));

        var result = await _userManager.ResetPasswordAsync(user, decodedToken, request.NewPassword);

        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            return (false, $"Password reset failed: {errors}");
        }

        return (true, "Password has been reset successfully");
    }

    public async Task<(bool Success, string Message)> ConfirmEmailAsync(ConfirmEmailRequest request)
    {
        var user = await _userManager.FindByIdAsync(request.UserId);
        if (user == null)
        {
            return (false, "Invalid request");
        }

        // Decode token from URL
        var decodedToken = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(request.Token));

        var result = await _userManager.ConfirmEmailAsync(user, decodedToken);

        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            return (false, $"Email confirmation failed: {errors}");
        }

        return (true, "Email confirmed successfully");
    }

    public async Task<(bool Success, string Message)> ResendEmailConfirmationAsync(ResendEmailConfirmationRequest request)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);

        // Don't reveal if user exists or not
        if (user == null)
        {
            return (true, "If the email exists and is not confirmed, a confirmation link has been sent.");
        }

        if (user.EmailConfirmed)
        {
            return (false, "Email is already confirmed");
        }

        var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);

        // Encode token for URL
        var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

        // TODO: Send email with confirmation link
        // Example link: https://yourdomain.com/confirm-email?userId={userId}&token={encodedToken}

        return (true, "If the email exists and is not confirmed, a confirmation link has been sent.");
    }

    public async Task<(bool Success, string Message, UserInfo? Data)> UpdateProfileAsync(Guid userId, UpdateProfileRequest request)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null)
        {
            return (false, "User not found", null);
        }

        var updated = false;

        // Update email if provided and different
        if (!string.IsNullOrWhiteSpace(request.Email) && request.Email != user.Email)
        {
            // Check if email is already taken
            var existingUser = await _userManager.FindByEmailAsync(request.Email);
            if (existingUser != null && existingUser.Id != userId)
            {
                return (false, "Email is already taken", null);
            }

            var token = await _userManager.GenerateChangeEmailTokenAsync(user, request.Email);
            var result = await _userManager.ChangeEmailAsync(user, request.Email, token);

            if (!result.Succeeded)
            {
                var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                return (false, $"Email update failed: {errors}", null);
            }

            updated = true;
        }

        // Update phone number if provided and different
        if (request.PhoneNumber != user.PhoneNumber)
        {
            user.PhoneNumber = request.PhoneNumber;
            var result = await _userManager.UpdateAsync(user);

            if (!result.Succeeded)
            {
                var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                return (false, $"Phone number update failed: {errors}", null);
            }

            updated = true;
        }

        if (!updated)
        {
            return (false, "No changes were made", null);
        }

        var userInfo = new UserInfo(
            user.Id,
            user.UserName!,
            user.Email!,
            user.EmailConfirmed,
            user.PhoneNumber,
            user.TwoFactorEnabled);

        return (true, "Profile updated successfully", userInfo);
    }
}
