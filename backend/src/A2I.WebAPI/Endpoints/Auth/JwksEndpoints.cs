using A2I.Application.Common;
using A2I.Infrastructure.Identity.Security;
using A2I.WebAPI.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;

namespace A2I.WebAPI.Endpoints.Auth;

public static class JwksEndpoints
{
    public static RouteGroupBuilder MapJwksEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/.well-known/jwks.json", GetJwks)
            .WithApiMetadata(
                "Get JWKS",
                "Retrieves the JSON Web Key Set (JWKS) for JWT token verification.")
            .Produces<object>()
            .Produces<ProblemDetails>(StatusCodes.Status500InternalServerError);
        
        group.MapGet("/jwks/rotate", RotateKey)
            .WithApiMetadata(
                "Rotate JWKS",
                "Generates a new key pair and adds it to the JWKS.")
            .Produces<object>()
            .Produces<ProblemDetails>(StatusCodes.Status500InternalServerError);
            
        return group;
    }
    
    private static IResult GetJwks(IKeyManagementService keyService)
    {
        var publicKeys = keyService.GetAllPublicKeys();
        
        var jwks = publicKeys.Select(key =>
        {
            var parameters = key.Rsa.ExportParameters(false);
            return new
            {
                kty = "RSA",
                use = "sig",
                alg = "RS256",
                kid = key.KeyId,
                n = Base64UrlEncoder.Encode(parameters.Modulus),
                e = Base64UrlEncoder.Encode(parameters.Exponent)
            };
        });

        return Results.Ok(new { keys = jwks });
    }
    
    private static async Task<IResult> RotateKey(IKeyManagementService keyService)
    {
        var newKey = await keyService.RotateKey();
        return Results.Ok(new 
        { 
            message = "Key rotated successfully",
            key_id = newKey.KeyId,
            created_at = newKey.CreatedAt
        });
    }
    
}