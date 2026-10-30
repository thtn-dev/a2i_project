using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Shell.Identity.Data;

namespace Shell.Identity.Pages.Account;

/// <summary>Ends the session on the authorization server (applications use <c>/connect/endsession</c>).</summary>
public sealed class LogoutModel(SignInManager<User> signIn) : PageModel
{
    public IActionResult OnGet() => LocalRedirect("/");

    public async Task<IActionResult> OnPostAsync()
    {
        await signIn.SignOutAsync();
        return LocalRedirect("/");
    }
}
