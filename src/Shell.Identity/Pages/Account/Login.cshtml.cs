using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Shell.Identity.Audit;
using Shell.Identity.Data;

namespace Shell.Identity.Pages.Account;

/// <summary>Password sign-in on the authorization server. Same message for every failure (no account enumeration).</summary>
public sealed class LoginModel(SignInManager<User> signIn, UserManager<User> users, AuditLog audit) : PageModel
{
    [BindProperty]
    public string Email { get; set; } = "";

    [BindProperty]
    public string Password { get; set; } = "";

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? Error { get; private set; }

    /// <summary>Social login providers that are configured (Google, Microsoft...).</summary>
    public IReadOnlyList<AuthenticationScheme> ExternalProviders { get; private set; } = [];

    public async Task OnGetAsync() => ExternalProviders = [.. await signIn.GetExternalAuthenticationSchemesAsync()];

    public async Task<IActionResult> OnPostAsync()
    {
        ExternalProviders = [.. await signIn.GetExternalAuthenticationSchemesAsync()];
        var returnUrl = Url.IsLocalUrl(ReturnUrl) ? ReturnUrl : "/";

        var user = await users.FindByEmailAsync(Email);
        if (user is not null && !user.IsDisabled)
        {
            var result = await signIn.PasswordSignInAsync(user, Password, isPersistent: false, lockoutOnFailure: true);
            if (result.Succeeded)
            {
                await audit.RecordAsync(AuditEvents.LoginSucceeded, user.Id, data: new { method = "pwd" });
                return LocalRedirect(returnUrl);
            }

            if (result.RequiresTwoFactor)
            {
                return RedirectToPage("LoginTwoFactor", new { returnUrl });
            }

            await audit.RecordAsync(result.IsLockedOut ? AuditEvents.LoginLockedOut : AuditEvents.LoginFailed, user.Id);
        }
        else
        {
            await audit.RecordAsync(AuditEvents.LoginFailed, user?.Id, data: new { email = Email.Trim().ToLowerInvariant() });
        }

        Error = "Invalid email or password, or the account is temporarily locked.";
        return Page();
    }
}
