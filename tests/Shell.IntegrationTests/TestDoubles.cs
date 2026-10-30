using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shell.Identity.Email;

namespace Shell.IntegrationTests;

/// <summary>Keeps the links the application would have emailed, per recipient.</summary>
public sealed class TestEmailSender : IAccountEmailSender
{
    private readonly ConcurrentDictionary<string, string> _lastLink = new(StringComparer.OrdinalIgnoreCase);

    public Task SendInvitationAsync(string email, string tenantName, string role, string link) => Keep(email, link);

    public Task SendSetPasswordAsync(string email, string link) => Keep(email, link);

    /// <summary>Path and query of the last link sent to <paramref name="email"/> (pages live on the authorization server host).</summary>
    public string LastLinkTo(string email) =>
        _lastLink.TryGetValue(email, out var link) ? new Uri(link).PathAndQuery : throw new InvalidOperationException($"No email sent to {email}.");

    private Task Keep(string email, string link)
    {
        _lastLink[email] = link;
        return Task.CompletedTask;
    }
}

/// <summary>
/// Stand-in for a social provider (Google...): the challenge immediately "returns" from the provider with the email and subject
/// given in request headers, exactly like a real remote handler signing into the external cookie.
/// </summary>
public sealed class FakeSocialHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "fake-social";
    public const string EmailHeader = "X-Fake-Email";
    public const string SubjectHeader = "X-Fake-Subject";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, Request.Headers[SubjectHeader].ToString()),
            new Claim(ClaimTypes.Email, Request.Headers[EmailHeader].ToString()),
            new Claim("email_verified", "true"),
        ], SchemeName);
        await Context.SignInAsync(IdentityConstants.ExternalScheme, new ClaimsPrincipal(identity), properties);
        Response.Redirect(properties.RedirectUri!);
    }
}

/// <summary>RFC 6238 TOTP (SHA-1, 30 s, 6 digits), as computed by authenticator apps.</summary>
public static class Totp
{
    public static string Code(string base32Key, DateTimeOffset? at = null)
    {
        var counter = (at ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds() / 30;
        var message = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(message);
        }

        var hash = HMACSHA1.HashData(Base32(base32Key), message);
        var offset = hash[^1] & 0x0F;
        var value = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (value % 1_000_000).ToString("D6");
    }

    private static byte[] Base32(string text)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bits = 0;
        var buffer = 0;
        var bytes = new List<byte>();
        foreach (var c in text.Trim().TrimEnd('=').ToUpperInvariant().Where(c => c != ' '))
        {
            buffer = (buffer << 5) | alphabet.IndexOf(c);
            bits += 5;
            if (bits >= 8)
            {
                bytes.Add((byte)(buffer >> (bits - 8)));
                bits -= 8;
            }
        }

        return [.. bytes];
    }
}
