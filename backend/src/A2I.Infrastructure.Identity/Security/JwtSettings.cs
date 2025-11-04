namespace A2I.Infrastructure.Identity.Security;

public class JwtSettings
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string KeyStorePath { get; set; } = "Keys/keystore.json";
    public int ExpirationMinutes { get; set; } = 60;
}