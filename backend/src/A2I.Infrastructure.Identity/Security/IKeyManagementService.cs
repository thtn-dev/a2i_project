using A2I.Infrastructure.Identity.Security.Models;
using Microsoft.IdentityModel.Tokens;

namespace A2I.Infrastructure.Identity.Security;

public interface IKeyManagementService
{
    /// <summary>
    /// Rotates the current key and generates a new RSA key pair
    /// </summary>
    /// <param name="keySize">Size of the RSA key (default: 2048)</param>
    /// <returns>Information about the newly generated key</returns>
    Task<RsaKeyInfo> RotateKey(int keySize = 2048);

    /// <summary>
    /// Gets the current active private key for signing tokens
    /// </summary>
    /// <returns>RSA security key for signing</returns>
    RsaSecurityKey GetCurrentPrivateKey();

    /// <summary>
    /// Gets the current active public key for token validation
    /// </summary>
    /// <returns>RSA security key for validation</returns>
    RsaSecurityKey GetCurrentPublicKey();

    /// <summary>
    /// Gets all valid public keys (not expired) for token validation
    /// </summary>
    /// <returns>List of all valid public keys</returns>
    List<RsaSecurityKey> GetAllPublicKeys();

    /// <summary>
    /// Gets the key store containing all keys
    /// </summary>
    /// <returns>The key store object</returns>
    KeyStore GetKeyStore();

    /// <summary>
    /// Gets the ID of the current active key
    /// </summary>
    /// <returns>Current key ID or empty string if no active key</returns>
    string GetCurrentKeyId();
}
