using A2I.Infrastructure.Identity.Entities;
using A2I.Infrastructure.Identity.Models;
using Microsoft.AspNetCore.Identity;
using System.Text;
using System.Web;
using A2I.Application.Common;
using FluentResults;

namespace A2I.Infrastructure.Identity.Services;

public interface ITwoFactorAuthService
{
    Task<Result<Enable2FAResponse>> Enable2FAAsync(Guid userId);
    Task<Result> Disable2FAAsync(Guid userId);
    Task<Result> Verify2FACodeAsync(Guid userId, string code);
    Task<Result<GenerateRecoveryCodesResponse>> GenerateRecoveryCodesAsync(Guid userId);
}
public class TwoFactorAuthService : ITwoFactorAuthService
{
    private readonly UserManager<ApplicationUser> _userManager;

    public TwoFactorAuthService(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<Result<Enable2FAResponse>> Enable2FAAsync(Guid userId)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        
        if (user is null)
            return Errors.NotFound("User not found");

        if (user.TwoFactorEnabled)
            return Errors.Conflict("Two-factor authentication is already enabled");

        await _userManager.ResetAuthenticatorKeyAsync(user);
        var authenticatorKey = await _userManager.GetAuthenticatorKeyAsync(user);

        if (string.IsNullOrEmpty(authenticatorKey))
            return Errors.Unexpected("Failed to generate authenticator key");

        var formattedKey = FormatKey(authenticatorKey);
        var authenticatorUri = GenerateQrCodeUri(user.Email!, authenticatorKey);

        var response = new Enable2FAResponse(formattedKey, authenticatorUri);

        return Result.Ok(response);
    }

    public async Task<Result> Disable2FAAsync(Guid userId)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        
        if (user is null)
            return Errors.NotFound("User not found");

        if (!user.TwoFactorEnabled)
            return Errors.Validation("Two-factor authentication is not enabled");

        var result = await _userManager.SetTwoFactorEnabledAsync(user, false);
        
        if (!result.Succeeded)
            return Errors.Unexpected("Failed to disable two-factor authentication");

        await _userManager.ResetAuthenticatorKeyAsync(user);

        return Result.Ok();
    }

    public async Task<Result> Verify2FACodeAsync(Guid userId, string code)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        
        if (user is null)
            return Errors.NotFound("User not found");

        var verificationCode = code.Replace(" ", string.Empty).Replace("-", string.Empty);

        var isValid = await _userManager.VerifyTwoFactorTokenAsync(
            user,
            _userManager.Options.Tokens.AuthenticatorTokenProvider,
            verificationCode);

        if (!isValid)
            return Errors.Validation("Invalid verification code");

        if (!user.TwoFactorEnabled)
        {
            var result = await _userManager.SetTwoFactorEnabledAsync(user, true);
            
            if (!result.Succeeded)
                return Errors.Unexpected("Failed to enable two-factor authentication");
        }

        return Result.Ok();
    }

    public async Task<Result<GenerateRecoveryCodesResponse>> GenerateRecoveryCodesAsync(Guid userId)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        
        if (user is null)
            return Errors.NotFound("User not found");

        if (!user.TwoFactorEnabled)
            return Errors.Validation("Two-factor authentication must be enabled first");

        var recoveryCodes = await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);

        if (recoveryCodes is null || !recoveryCodes.Any())
            return Errors.Unexpected("Failed to generate recovery codes");

        var response = new GenerateRecoveryCodesResponse(recoveryCodes.ToArray());

        return Result.Ok(response);
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
