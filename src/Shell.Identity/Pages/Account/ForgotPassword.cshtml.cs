using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Shell.Identity.Audit;
using Shell.Identity.Data;
using Shell.Identity.Email;

namespace Shell.Identity.Pages.Account;

/// <summary>Emails a set-password link. Same answer whether the account exists or not.</summary>
public sealed class ForgotPasswordModel(UserManager<User> users, IAccountEmailSender emails, AccountLinks links) : PageModel
{
    [BindProperty]
    public string Email { get; set; } = "";

    public bool Sent { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (await users.FindByEmailAsync(Email) is { IsDisabled: false } user)
        {
            await emails.SendSetPasswordAsync(user.Email!, links.SetPassword(user.Id, await users.GeneratePasswordResetTokenAsync(user)));
        }

        Sent = true;
        return Page();
    }
}

/// <summary>
/// Sets a password from an emailed link (forgotten password, imported account). Proves ownership of the email.
/// Changes the security stamp, which ends the account's other sessions.
/// </summary>
public sealed class SetPasswordModel(UserManager<User> users, AuditLog audit) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public Guid UserId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string Token { get; set; } = "";

    [BindProperty]
    public string Password { get; set; } = "";

    public List<string> Errors { get; } = [];

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await users.FindByIdAsync(UserId.ToString());
        var result = user is null || user.IsDisabled
            ? IdentityResult.Failed(new IdentityError { Description = "This link is invalid or expired." })
            : await users.ResetPasswordAsync(user, Token, Password);
        if (!result.Succeeded)
        {
            Errors.AddRange(result.Errors.Select(e => e.Code == "InvalidToken" ? "This link is invalid or expired." : e.Description));
            return Page();
        }

        user!.EmailConfirmed = true;
        audit.Add(AuditEvents.PasswordReset, user.Id);
        await users.UpdateAsync(user);
        return RedirectToPage("Login");
    }
}
