using A2I.Infrastructure.Identity.Entities;
using A2I.Infrastructure.Identity.Models;
using Microsoft.AspNetCore.Identity;
using System.Text;
using System.Web;

namespace A2I.Infrastructure.Identity.Services;

public interface ITwoFactorAuthService
{
    Task<(bool Success, string Message, Enable2FAResponse? Data)> Enable2FAAsync(Guid userId);
    Task<(bool Success, string Message)> Disable2FAAsync(Guid userId);
    Task<(bool Success, string Message)> Verify2FACodeAsync(Guid userId, string code);
    Task<(bool Success, string Message, GenerateRecoveryCodesResponse? Data)> GenerateRecoveryCodesAsync(Guid userId);
}

public class TwoFactorAuthService : ITwoFactorAuthService
{
    private readonly UserManager<ApplicationUser> _userManager;

    public TwoFactorAuthService(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<(bool Success, string Message, Enable2FAResponse? Data)> Enable2FAAsync(Guid userId)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null)
        {
            return (false, "User not found", null);
        }

        if (user.TwoFactorEnabled)
        {
            return (false, "Two-factor authentication is already enabled", null);
        }

        // Generate authenticator key
        await _userManager.ResetAuthenticatorKeyAsync(user);
        var authenticatorKey = await _userManager.GetAuthenticatorKeyAsync(user);

        if (string.IsNullOrEmpty(authenticatorKey))
        {
            return (false, "Failed to generate authenticator key", null);
        }

        // Format key for display (groups of 4)
        var formattedKey = FormatKey(authenticatorKey);

        // Generate QR code URI for authenticator apps
        var authenticatorUri = GenerateQrCodeUri(user.Email!, authenticatorKey);

        var response = new Enable2FAResponse(formattedKey, authenticatorUri);

        return (true, "Scan the QR code or enter the key manually in your authenticator app, then verify with a code", response);
    }

    public async Task<(bool Success, string Message)> Disable2FAAsync(Guid userId)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null)
        {
            return (false, "User not found");
        }

        if (!user.TwoFactorEnabled)
        {
            return (false, "Two-factor authentication is not enabled");
        }

        var result = await _userManager.SetTwoFactorEnabledAsync(user, false);
        if (!result.Succeeded)
        {
            return (false, "Failed to disable two-factor authentication");
        }

        // Reset authenticator key
        await _userManager.ResetAuthenticatorKeyAsync(user);

        return (true, "Two-factor authentication has been disabled");
    }

    public async Task<(bool Success, string Message)> Verify2FACodeAsync(Guid userId, string code)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null)
        {
            return (false, "User not found");
        }

        // Remove spaces and validate format
        var verificationCode = code.Replace(" ", string.Empty).Replace("-", string.Empty);

        var isValid = await _userManager.VerifyTwoFactorTokenAsync(
            user,
            _userManager.Options.Tokens.AuthenticatorTokenProvider,
            verificationCode);

        if (!isValid)
        {
            return (false, "Invalid verification code");
        }

        // Enable 2FA if this is the first verification
        if (!user.TwoFactorEnabled)
        {
            var result = await _userManager.SetTwoFactorEnabledAsync(user, true);
            if (!result.Succeeded)
            {
                return (false, "Failed to enable two-factor authentication");
            }
        }

        return (true, "Two-factor authentication verified successfully");
    }

    public async Task<(bool Success, string Message, GenerateRecoveryCodesResponse? Data)> GenerateRecoveryCodesAsync(Guid userId)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null)
        {
            return (false, "User not found", null);
        }

        if (!user.TwoFactorEnabled)
        {
            return (false, "Two-factor authentication must be enabled first", null);
        }

        var recoveryCodes = await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);

        if (recoveryCodes == null || !recoveryCodes.Any())
        {
            return (false, "Failed to generate recovery codes", null);
        }

        var response = new GenerateRecoveryCodesResponse(recoveryCodes.ToArray());

        return (true, "Recovery codes generated successfully. Store them in a safe place.", response);
    }

    private static string FormatKey(string key)
    {
        var result = new StringBuilder();
        int currentPosition = 0;

        while (currentPosition + 4 < key.Length)
        {
            result.Append(key.AsSpan(currentPosition, 4)).Append(' ');
            currentPosition += 4;
        }

        if (currentPosition < key.Length)
        {
            result.Append(key.AsSpan(currentPosition));
        }

        return result.ToString().ToLowerInvariant();
    }

    private string GenerateQrCodeUri(string email, string unformattedKey)
    {
        const string authenticatorUriFormat = "otpauth://totp/{0}:{1}?secret={2}&issuer={0}&digits=6";
        return string.Format(
            authenticatorUriFormat,
            HttpUtility.UrlEncode("A2I"),
            HttpUtility.UrlEncode(email),
            unformattedKey);
    }
}
