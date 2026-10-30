using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Shell.Identity.Audit;
using Shell.Identity.Data;

namespace Shell.Identity.Pages.Account;

/// <summary>Second step after a valid password when MFA is on. Wrong codes count towards the account lockout.</summary>
public sealed class LoginTwoFactorModel(SignInManager<User> signIn, AuditLog audit) : PageModel
{
    [BindProperty]
    public string Code { get; set; } = "";

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync() =>
        await signIn.GetTwoFactorAuthenticationUserAsync() is null ? RedirectToPage("Login") : Page();

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await signIn.GetTwoFactorAuthenticationUserAsync();
        if (user is null || user.IsDisabled)
        {
            return RedirectToPage("Login");
        }

        var result = await signIn.TwoFactorAuthenticatorSignInAsync(MfaCodes.Clean(Code), isPersistent: false, rememberClient: false);
        return await TwoFactorResult.HandleAsync(this, result, user, audit, "totp", ReturnUrl, error => Error = error);
    }
}

/// <summary>Recovery code instead of the authenticator (each code works once).</summary>
public sealed class LoginRecoveryModel(SignInManager<User> signIn, AuditLog audit) : PageModel
{
    [BindProperty]
    public string Code { get; set; } = "";

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync() =>
        await signIn.GetTwoFactorAuthenticationUserAsync() is null ? RedirectToPage("Login") : Page();

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await signIn.GetTwoFactorAuthenticationUserAsync();
        if (user is null || user.IsDisabled)
        {
            return RedirectToPage("Login");
        }

        var result = await signIn.TwoFactorRecoveryCodeSignInAsync(Code.Replace(" ", "", StringComparison.Ordinal));
        return await TwoFactorResult.HandleAsync(this, result, user, audit, "recovery_code", ReturnUrl, error => Error = error);
    }
}

internal static class TwoFactorResult
{
    public static async Task<IActionResult> HandleAsync(
        PageModel page, Microsoft.AspNetCore.Identity.SignInResult result, User user, AuditLog audit, string method, string? returnUrl, Action<string> setError)
    {
        if (result.Succeeded)
        {
            await audit.RecordAsync(AuditEvents.LoginSucceeded, user.Id, data: new { method = "mfa", factor = method });
            return page.LocalRedirect(page.Url.IsLocalUrl(returnUrl) ? returnUrl : "/");
        }

        await audit.RecordAsync(result.IsLockedOut ? AuditEvents.LoginLockedOut : AuditEvents.LoginFailed, user.Id, data: new { method });
        setError(result.IsLockedOut ? "The account is temporarily locked." : "Invalid code.");
        return page.Page();
    }
}

internal static class MfaCodes
{
    public static string Clean(string code) => code.Replace(" ", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal);
}
