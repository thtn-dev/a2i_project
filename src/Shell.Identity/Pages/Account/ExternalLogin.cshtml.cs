using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Shell.Identity.Audit;
using Shell.Identity.Data;

namespace Shell.Identity.Pages.Account;

/// <summary>
/// Social login (Google, Microsoft...) for personal accounts. A login already linked signs in; an unknown login with a new email
/// creates a personal account. An unknown login whose email belongs to an existing account is refused: the owner must sign in
/// first and link it (prevents taking over an account through a provider that asserts someone else's email).
/// Accounts managed by an organization cannot use social login.
/// </summary>
public sealed class ExternalLoginModel(SignInManager<User> signIn, UserManager<User> users, AuditLog audit, TimeProvider time) : PageModel
{
    public string? Error { get; private set; }

    public IActionResult OnGet() => RedirectToPage("Login");

    public async Task<IActionResult> OnPostAsync(string provider, string? returnUrl)
    {
        if (!(await signIn.GetExternalAuthenticationSchemesAsync()).Any(s => s.Name == provider))
        {
            return RedirectToPage("Login");
        }

        var properties = signIn.ConfigureExternalAuthenticationProperties(provider, Url.Page("ExternalLogin", "Callback", new { returnUrl }));
        return Challenge(properties, provider);
    }

    public async Task<IActionResult> OnGetCallbackAsync(string? returnUrl, string? remoteError)
    {
        returnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : "/";
        var info = remoteError is null ? await signIn.GetExternalLoginInfoAsync() : null;
        if (info is null)
        {
            return Fail("The external sign-in failed or was cancelled.");
        }

        var linked = await users.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
        if (linked is not null)
        {
            if (linked.IsDisabled || linked.ManagedByTenantId is not null)
            {
                await audit.RecordAsync(AuditEvents.ExternalLoginRejected, linked.Id, data: new { provider = info.LoginProvider, reason = "account not allowed" });
                return await FailAsync("This account cannot sign in with this provider.");
            }

            var result = await signIn.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, isPersistent: false, bypassTwoFactor: false);
            if (result.Succeeded)
            {
                await audit.RecordAsync(AuditEvents.LoginSucceeded, linked.Id, data: new { method = info.LoginProvider });
                return LocalRedirect(returnUrl);
            }

            return result.RequiresTwoFactor
                ? RedirectToPage("LoginTwoFactor", new { returnUrl })
                : await FailAsync("The account is temporarily locked.");
        }

        var email = info.Principal.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrWhiteSpace(email))
        {
            return await FailAsync($"{info.ProviderDisplayName} did not provide an email address.");
        }

        if (await users.FindByEmailAsync(email) is { } existing)
        {
            await audit.RecordAsync(AuditEvents.ExternalLoginRejected, existing.Id, data: new { provider = info.LoginProvider, reason = "email of an existing account" });
            return await FailAsync(
                $"An account with this email already exists. Sign in to it, then link {info.ProviderDisplayName} from your account page.");
        }

        var user = new User
        {
            Id = Guid.CreateVersion7(),
            UserName = email,
            Email = email,
            EmailConfirmed = info.Principal.FindFirstValue("email_verified") == "true",
            CreatedAt = time.GetUtcNow(),
        };
        var created = await users.CreateAsync(user);
        if (!created.Succeeded || !(await users.AddLoginAsync(user, info)).Succeeded)
        {
            return await FailAsync("The account could not be created.");
        }

        audit.Add(AuditEvents.UserRegistered, user.Id, data: new { method = info.LoginProvider });
        await audit.RecordAsync(AuditEvents.ExternalLoginLinked, user.Id, data: new { provider = info.LoginProvider });
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        await signIn.SignInAsync(user, isPersistent: false, authenticationMethod: info.LoginProvider);
        return LocalRedirect(returnUrl);
    }

    private async Task<IActionResult> FailAsync(string error)
    {
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        return Fail(error);
    }

    private PageResult Fail(string error)
    {
        Error = error;
        return Page();
    }
}
