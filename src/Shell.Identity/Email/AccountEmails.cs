using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Shell.Identity.Email;

/// <summary>
/// Sends account emails. The default implementation only logs the links (development, demo);
/// production registers an implementation backed by a real provider (SMTP, SendGrid, SES...).
/// </summary>
public interface IAccountEmailSender
{
    Task SendInvitationAsync(string email, string tenantName, string role, string link);

    /// <summary>Password reset, or first password of an imported account.</summary>
    Task SendSetPasswordAsync(string email, string link);
}

internal sealed class LoggingAccountEmailSender(ILogger<LoggingAccountEmailSender> logger) : IAccountEmailSender
{
    public Task SendInvitationAsync(string email, string tenantName, string role, string link)
    {
        logger.LogWarning("No email provider configured. Invitation of {Email} to {Tenant} as {Role}: {Link}", email, tenantName, role, link);
        return Task.CompletedTask;
    }

    public Task SendSetPasswordAsync(string email, string link)
    {
        logger.LogWarning("No email provider configured. Set-password link for {Email}: {Link}", email, link);
        return Task.CompletedTask;
    }
}

/// <summary>Absolute links to pages of the authorization server, put in emails.</summary>
public sealed class AccountLinks(IOptions<ShellIdentityOptions> options)
{
    public string Invitation(string token) => Build($"invitations/accept?token={Uri.EscapeDataString(token)}");

    public string SetPassword(Guid userId, string token) =>
        Build($"account/set-password?userId={userId}&token={Uri.EscapeDataString(token)}");

    private string Build(string relative) => new Uri(options.Value.Issuer!, relative).ToString();
}
