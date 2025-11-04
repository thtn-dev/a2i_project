using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace A2I.Infrastructure.Identity.Security;

public interface IJwtService
{
    string GenerateToken(string userId, string username, List<string> roles);
}

public class JwtService : IJwtService
{
    private readonly IConfiguration _config;
    private readonly KeyManagementService _keyService;

    public JwtService(IConfiguration config, KeyManagementService keyService)
    {
        _config = config;
        _keyService = keyService;
    }

    public string GenerateToken(string userId, string username, List<string> roles)
    {
        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId),
            new Claim(JwtRegisteredClaimNames.UniqueName, username),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var privateKey = _keyService.GetCurrentPrivateKey();
        var credentials = new SigningCredentials(privateKey, SecurityAlgorithms.RsaSha256);

        var header = new JwtHeader(credentials);

        var payload = new JwtPayload(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddMinutes(int.Parse(_config["Jwt:ExpirationMinutes"] ?? "60"))
        );

        var token = new JwtSecurityToken(header, payload);
    
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}