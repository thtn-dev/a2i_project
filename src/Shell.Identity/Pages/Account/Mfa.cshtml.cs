using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Shell.Identity.Audit;
using Shell.Identity.Data;

namespace Shell.Identity.Pages.Account;

/// <summary>Turns TOTP two-factor authentication on (with recovery codes) or off. Both need a valid current code.</summary>
[Authorize]
public sealed class MfaModel(UserManager<User> users, SignInManager<User> signIn, AuditLog audit) : PageModel
{
    [BindProperty]
    public string Code { get; set; } = "";

    public bool Enabled { get; private set; }

    public string? SharedKey { get; private set; }

    public string? AuthenticatorUri { get; private set; }

    public IReadOnlyList<string> RecoveryCodes { get; private set; } = [];

    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await users.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        await LoadAsync(user);
        return Page();
    }

    public async Task<IActionResult> OnPostEnableAsync()
    {
        var user = await users.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        if (!await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, MfaCodes.Clean(Code)))
        {
            Error = "Invalid code.";
            await LoadAsync(user);
            return Page();
        }

        await users.SetTwoFactorEnabledAsync(user, true);
        RecoveryCodes = [.. await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10) ?? []];
        await audit.RecordAsync(AuditEvents.MfaEnabled, user.Id);
        await signIn.RefreshSignInAsync(user); // The security stamp changed: other sessions end, this one continues.
        Enabled = true;
        return Page();
    }

    public async Task<IActionResult> OnPostDisableAsync()
    {
        var user = await users.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        if (!await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, MfaCodes.Clean(Code)))
        {
            Error = "Invalid code.";
            await LoadAsync(user);
            return Page();
        }

        await users.SetTwoFactorEnabledAsync(user, false);
        await users.ResetAuthenticatorKeyAsync(user);
        await audit.RecordAsync(AuditEvents.MfaDisabled, user.Id);
        await signIn.RefreshSignInAsync(user);
        await LoadAsync(user);
        return Page();
    }

    private async Task LoadAsync(User user)
    {
        Enabled = await users.GetTwoFactorEnabledAsync(user);
        if (Enabled)
        {
            return;
        }

        SharedKey = await users.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(SharedKey))
        {
            await users.ResetAuthenticatorKeyAsync(user);
            await signIn.RefreshSignInAsync(user); // New security stamp: keep this session valid.
            SharedKey = await users.GetAuthenticatorKeyAsync(user);
        }

        AuthenticatorUri = $"otpauth://totp/Shell:{Uri.EscapeDataString(user.Email!)}?secret={SharedKey}&issuer=Shell&digits=6";
    }
}
