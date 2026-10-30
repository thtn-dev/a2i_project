using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Shell.Identity.Audit;
using Shell.Identity.Data;

namespace Shell.Identity.Pages.Account;

/// <summary>Links or unlinks social logins of the signed-in personal account (the explicit way to attach a provider to an existing account).</summary>
[Authorize]
public sealed class LoginsModel(SignInManager<User> signIn, UserManager<User> users, AuditLog audit) : PageModel
{
    public IList<UserLoginInfo> Linked { get; private set; } = [];

    public IReadOnlyList<AuthenticationScheme> Available { get; private set; } = [];

    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync() => await users.GetUserAsync(User) is { } user ? await ShowAsync(user) : Challenge();

    public async Task<IActionResult> OnPostLinkAsync(string provider)
    {
        var user = await users.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        if (user.ManagedByTenantId is not null)
        {
            return await ShowAsync(user, "Accounts managed by an organization cannot link social logins.");
        }

        var properties = signIn.ConfigureExternalAuthenticationProperties(provider, Url.Page("Logins", "LinkCallback"), user.Id.ToString());
        return Challenge(properties, provider);
    }

    public async Task<IActionResult> OnGetLinkCallbackAsync()
    {
        var user = await users.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        var info = await signIn.GetExternalLoginInfoAsync(user.Id.ToString());
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        if (info is null || user.ManagedByTenantId is not null)
        {
            return await ShowAsync(user, "The login could not be linked.");
        }

        if (!(await users.AddLoginAsync(user, info)).Succeeded)
        {
            return await ShowAsync(user, "This login is already linked to another account.");
        }

        await audit.RecordAsync(AuditEvents.ExternalLoginLinked, user.Id, data: new { provider = info.LoginProvider });
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRemoveAsync(string provider, string key)
    {
        var user = await users.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        // Never leave an account without any way to sign in.
        if (!await users.HasPasswordAsync(user) && (await users.GetLoginsAsync(user)).Count <= 1)
        {
            return await ShowAsync(user, "Set a password before removing your only login.");
        }

        if ((await users.RemoveLoginAsync(user, provider, key)).Succeeded)
        {
            await audit.RecordAsync(AuditEvents.ExternalLoginRemoved, user.Id, data: new { provider });
            await signIn.RefreshSignInAsync(user);
        }

        return RedirectToPage();
    }

    private async Task<PageResult> ShowAsync(User user, string? error = null)
    {
        Error = error;
        Linked = await users.GetLoginsAsync(user);
        Available = [.. (await signIn.GetExternalAuthenticationSchemesAsync()).Where(s => Linked.All(l => l.LoginProvider != s.Name))];
        return Page();
    }
}
