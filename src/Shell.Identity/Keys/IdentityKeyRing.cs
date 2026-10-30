using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Shell.Identity.Keys;

/// <summary>
/// Token signing and encryption certificates loaded from <see cref="ShellIdentityOptions.KeyOptions.Path"/>, shared by every instance
/// (a token signed by one instance must validate on all of them). Ordered newest first.
/// </summary>
public sealed class IdentityKeyRing
{
    private const string SigningPrefix = "signing-";
    private const string EncryptionPrefix = "encryption-";

    public IdentityKeyRing(IOptions<ShellIdentityOptions> options, IHostEnvironment environment, TimeProvider time, ILogger<IdentityKeyRing> logger)
    {
        var keys = options.Value.Keys;
        var directory = Path.GetFullPath(keys.Path, environment.ContentRootPath);

        if (keys.AutoGenerate)
        {
            EnsureExists(directory, SigningPrefix, X509KeyUsageFlags.DigitalSignature, keys.Password, time, logger);
            EnsureExists(directory, EncryptionPrefix, X509KeyUsageFlags.KeyEncipherment, keys.Password, time, logger);
        }

        Signing = Load(directory, SigningPrefix, keys.Password);
        Encryption = Load(directory, EncryptionPrefix, keys.Password);
    }

    public IReadOnlyList<X509Certificate2> Signing { get; }

    public IReadOnlyList<X509Certificate2> Encryption { get; }

    private static List<X509Certificate2> Load(string directory, string prefix, string? password)
    {
        var files = Directory.Exists(directory) ? Directory.GetFiles(directory, $"{prefix}*.pfx") : [];
        if (files.Length == 0)
        {
            throw new InvalidOperationException(
                $"No '{prefix}*.pfx' certificate in '{directory}'. Mount the token certificates there (Identity:Keys:Path), " +
                "or set Identity:Keys:AutoGenerate=true in development.");
        }

        return files
            .Select(file => X509CertificateLoader.LoadPkcs12FromFile(file, password, X509KeyStorageFlags.EphemeralKeySet))
            .OrderByDescending(certificate => certificate.NotAfter)
            .ToList();
    }

    /// <summary>Self-signed certificate written atomically, so concurrent instances end up with exactly one.</summary>
    private static void EnsureExists(
        string directory, string prefix, X509KeyUsageFlags usage, string? password, TimeProvider time, ILogger logger)
    {
        Directory.CreateDirectory(directory);
        if (Directory.GetFiles(directory, $"{prefix}*.pfx").Length > 0)
        {
            return;
        }

        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest($"CN=Shell token {prefix.TrimEnd('-')}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(usage, critical: true));
        var now = time.GetUtcNow();
        using var certificate = request.CreateSelfSigned(now.AddMinutes(-5), now.AddYears(2));

        var temporary = Path.Combine(directory, $"{Guid.NewGuid():N}.tmp");
        File.WriteAllBytes(temporary, certificate.Export(X509ContentType.Pfx, password));
        try
        {
            File.Move(temporary, Path.Combine(directory, $"{prefix}auto.pfx"));
            logger.LogWarning("Generated a self-signed {Prefix} certificate in {Directory} (Identity:Keys:AutoGenerate; not for production)",
                prefix.TrimEnd('-'), directory);
        }
        catch (IOException)
        {
            File.Delete(temporary); // Another instance won the race.
        }
    }
}
