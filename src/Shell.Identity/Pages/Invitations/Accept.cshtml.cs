using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Shell.Core.Tenancy;
using Shell.Identity.Access;
using Shell.Identity.Data;

namespace Shell.Identity.Pages.Invitations;

/// <summary>
/// Landing page of an invitation email. New people create an account first, existing users just sign in; then accept.
/// The signed-in account must have the invited email (see <see cref="MembershipService.AcceptAsync"/>).
/// </summary>
public sealed class AcceptModel(MembershipService memberships, TenantStore tenants, UserManager<User> users) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string Token { get; set; } = "";

    public string? TenantName { get; private set; }

    public string? Role { get; private set; }

    public bool Accepted { get; private set; }

    public string? Error { get; private set; }

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await users.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        var invitation = await LoadAsync();
        if (invitation is null)
        {
            return Page();
        }

        var result = await memberships.AcceptAsync(Token, user, HttpContext.RequestAborted);
        Accepted = result.Outcome == IamOutcome.Ok;
        Error = result.Detail;
        return Page();
    }

    private async Task<TenantInvitation?> LoadAsync()
    {
        var invitation = await memberships.FindPendingAsync(Token, HttpContext.RequestAborted);
        if (invitation is null || await tenants.FindByIdAsync(invitation.TenantId, HttpContext.RequestAborted) is not { IsActive: true } tenant)
        {
            Error = "This invitation is invalid, expired or already used.";
            return null;
        }

        TenantName = tenant.Identifier;
        Role = invitation.Role;
        return invitation;
    }
}
