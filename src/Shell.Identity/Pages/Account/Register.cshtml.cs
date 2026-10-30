using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Shell.Identity.Audit;
using Shell.Identity.Data;

namespace Shell.Identity.Pages.Account;

/// <summary>Creates a personal account. It belongs to no tenant until a tenant invites it.</summary>
public sealed class RegisterModel(SignInManager<User> signIn, UserManager<User> users, AuditLog audit, TimeProvider time) : PageModel
{
    [BindProperty]
    public string Email { get; set; } = "";

    [BindProperty]
    public string Password { get; set; } = "";

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public List<string> Errors { get; } = [];

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var user = new User { Id = Guid.CreateVersion7(), UserName = Email, Email = Email, CreatedAt = time.GetUtcNow() };
        var result = await users.CreateAsync(user, Password);
        if (!result.Succeeded)
        {
            Errors.AddRange(result.Errors.Select(e => e.Description));
            return Page();
        }

        await audit.RecordAsync(AuditEvents.UserRegistered, user.Id, data: new { method = "pwd" });
        await signIn.SignInAsync(user, isPersistent: false, authenticationMethod: "pwd");
        return LocalRedirect(Url.IsLocalUrl(ReturnUrl) ? ReturnUrl : "/");
    }
}
