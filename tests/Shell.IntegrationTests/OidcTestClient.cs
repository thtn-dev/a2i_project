using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.WebUtilities;
using Shell.Core.Tenancy;
using Shell.Identity.Clients;

namespace Shell.IntegrationTests;

/// <summary>
/// Plays a tenant SPA against the authorization server: Authorization Code + PKCE through the real login page
/// (cookies and antiforgery included), then the token endpoint.
/// </summary>
public sealed partial class OidcTestClient(ShellHostFactory host)
{
    private const string Scopes = "openid offline_access api";

    /// <summary>Browser of the user: keeps the authorization server session cookie between calls.</summary>
    private readonly HttpClient _browser = host.CreateClient(new()
    {
        BaseAddress = new Uri($"http://{ShellHostFactory.AuthHost}"),
        AllowAutoRedirect = false,
        HandleCookies = true,
    });

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static string RedirectUri(string domain) => $"http://{domain}/signin-callback";

    /// <summary>Signs in (if not yet) and returns tokens for the tenant owning <paramref name="tenantId"/>'s client.</summary>
    public async Task<TokenResponse> GetTokensAsync(string email, string password, Guid tenantId, string domain)
    {
        var result = await AuthorizeAsync(email, password, tenantId, domain);
        Assert.True(result.Code is not null, $"No authorization code: {result.Error}");
        return await RedeemAsync(tenantId, domain, result.Code!, result.Verifier);
    }

    /// <summary>Authorization request; signs in on the login page when the authorization server asks for it.</summary>
    public async Task<AuthorizeResult> AuthorizeAsync(string email, string password, Guid tenantId, string domain, string? redirectUri = null)
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var authorize = QueryHelpers.AddQueryString("/connect/authorize", new Dictionary<string, string?>
        {
            ["client_id"] = TenantClients.ClientIdFor(tenantId),
            ["response_type"] = "code",
            ["scope"] = Scopes,
            ["redirect_uri"] = redirectUri ?? RedirectUri(domain),
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["state"] = "test-state",
        });

        var response = await _browser.GetAsync(authorize, Ct);
        if (response.Headers.Location is { } login && login.OriginalString.Contains("/account/login", StringComparison.OrdinalIgnoreCase))
        {
            await SignInAsync(login.OriginalString, email, password);
            response = await _browser.GetAsync(authorize, Ct);
        }

        if (response.StatusCode != HttpStatusCode.Redirect || response.Headers.Location is null)
        {
            return new AuthorizeResult(null, verifier, $"HTTP {(int)response.StatusCode}", response.StatusCode, null);
        }

        var location = response.Headers.Location;
        var query = QueryHelpers.ParseQuery(location.Query);
        return new AuthorizeResult(
            query.TryGetValue("code", out var code) ? code.ToString() : null,
            verifier,
            query.TryGetValue("error", out var error) ? error.ToString() : null,
            response.StatusCode,
            location);
    }

    public async Task<TokenResponse> RedeemAsync(Guid tenantId, string domain, string code, string verifier)
    {
        var response = await _browser.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = TenantClients.ClientIdFor(tenantId),
            ["code"] = code,
            ["code_verifier"] = verifier,
            ["redirect_uri"] = RedirectUri(domain),
        }), Ct);
        return await ReadTokensAsync(response);
    }

    public async Task<TokenResponse> RefreshAsync(Guid tenantId, string refreshToken)
    {
        var response = await _browser.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = TenantClients.ClientIdFor(tenantId),
            ["refresh_token"] = refreshToken,
        }), Ct);
        return await ReadTokensAsync(response);
    }

    /// <summary>Calls a tenant API on its real host name, with an optional bearer token.</summary>
    public static async Task<HttpResponseMessage> CallApiAsync(
        ShellHostFactory host, string domain, HttpMethod method, string path, string? accessToken, object? body = null, bool viaHeader = false)
    {
        var client = host.CreateClient(new() { BaseAddress = new Uri(viaHeader ? "http://localhost" : $"http://{domain}") });
        using var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body) };
        if (viaHeader)
        {
            request.Headers.Add(TenantResolutionMiddleware.TenantHostHeader, domain);
        }

        if (accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        return await client.SendAsync(request, Ct);
    }

    private async Task SignInAsync(string loginUrl, string email, string password)
    {
        var response = await SubmitLoginAsync(email, password, loginUrl);
        Assert.True(response.StatusCode == HttpStatusCode.Redirect, $"Sign-in of {email} failed (HTTP {(int)response.StatusCode}).");
    }

    /// <summary>Posts the login form; a redirect means success (or the MFA step), 200 means the page shows an error.</summary>
    public Task<HttpResponseMessage> SubmitLoginAsync(string email, string password, string loginUrl = "/account/login")
    {
        var returnUrl = QueryHelpers.ParseQuery(new Uri(new Uri("http://x"), loginUrl).Query).TryGetValue("ReturnUrl", out var value) ? value.ToString() : "";
        return PostFormAsync(loginUrl, new() { ["Email"] = email, ["Password"] = password, ["ReturnUrl"] = returnUrl });
    }

    /// <summary>Creates a personal account through the registration page and stays signed in.</summary>
    public async Task RegisterAsync(string email, string password)
    {
        var response = await PostFormAsync("/account/register", new() { ["Email"] = email, ["Password"] = password });
        Assert.True(response.StatusCode == HttpStatusCode.Redirect, $"Registration of {email} failed: {await response.Content.ReadAsStringAsync(Ct)}");
    }

    public async Task<string> GetPageAsync(string url) => await (await _browser.GetAsync(url, Ct)).Content.ReadAsStringAsync(Ct);

    public Task<HttpResponseMessage> GetAsync(string url) => _browser.GetAsync(url, Ct);

    /// <summary>
    /// Submits a form of the authorization server like a browser: takes the antiforgery token from <paramref name="tokenPage"/>
    /// (default: the same URL), then posts the fields.
    /// </summary>
    public async Task<HttpResponseMessage> PostFormAsync(
        string url, Dictionary<string, string> fields, string? tokenPage = null, IReadOnlyDictionary<string, string>? headers = null)
    {
        var antiforgery = AntiforgeryField().Match(await GetPageAsync(tokenPage ?? url));
        Assert.True(antiforgery.Success, $"No antiforgery token on {tokenPage ?? url}.");
        fields["__RequestVerificationToken"] = WebUtility.HtmlDecode(antiforgery.Groups[1].Value);

        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(fields) };
        foreach (var (name, value) in headers ?? new Dictionary<string, string>())
        {
            request.Headers.Add(name, value);
        }

        return await _browser.SendAsync(request, Ct);
    }

    private static async Task<TokenResponse> ReadTokensAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<TokenResponse>(Ct))! with { StatusCode = response.StatusCode };

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryField();
}

public sealed record AuthorizeResult(string? Code, string Verifier, string? Error, HttpStatusCode StatusCode, Uri? Location);

public sealed record TokenResponse(
    [property: JsonPropertyName("access_token")] string? AccessToken,
    [property: JsonPropertyName("refresh_token")] string? RefreshToken,
    [property: JsonPropertyName("error")] string? Error)
{
    public HttpStatusCode StatusCode { get; init; }
}
