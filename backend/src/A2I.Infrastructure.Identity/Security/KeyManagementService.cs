using System.Security.Cryptography;
using System.Text.Json;
using A2I.Infrastructure.Identity.Security.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace A2I.Infrastructure.Identity.Security;

public class KeyManagementService : IKeyManagementService
{
    private readonly string _keyStorePath;
    private readonly ILogger<KeyManagementService> _logger;
    private readonly KeyStore _keyStore;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public KeyManagementService(
        IOptions<JwtSettings> jwtOptions,
        ILogger<KeyManagementService> logger)
    {
        var jwtSettings = jwtOptions.Value;
        _keyStorePath = jwtSettings.KeyStorePath;
        _logger = logger;
        
        EnsureDirectoryExists();
        _keyStore = LoadKeyStore();

        if (_keyStore.CurrentKey != null)
        {
            return;
        }
        
        _logger.LogInformation("No existing keys found. Generating initial key...");
        RotateKey().GetAwaiter().GetResult();
    }

    private void EnsureDirectoryExists()
    {
        var directory = Path.GetDirectoryName(_keyStorePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private KeyStore LoadKeyStore()
    {
        if (!File.Exists(_keyStorePath))
        {
            return new KeyStore();
        }

        try
        {
            var json = File.ReadAllText(_keyStorePath);
            return JsonSerializer.Deserialize<KeyStore>(json) ?? new KeyStore();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load key store. Creating new one.");
            return new KeyStore();
        }
    }

    private void SaveKeyStore()
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(_keyStore, options);
        File.WriteAllText(_keyStorePath, json);
        _logger.LogInformation("Key store saved to {Path}", _keyStorePath);
    }

    public async Task<RsaKeyInfo> RotateKey(int keySize = 2048)
    {
        await _lock.WaitAsync();
        try
        {
            _logger.LogInformation("Starting key rotation...");

            if (_keyStore.CurrentKey != null)
            {
                _keyStore.CurrentKey.IsActive = false;
            }

            // Tạo key mới
            var newKey = GenerateRsaKey(keySize);
            
            // Set làm current key
            _keyStore.CurrentKey = newKey;
            _keyStore.Keys.Add(newKey);
            _keyStore.LastRotation = DateTime.UtcNow;

            CleanupOldKeys(keepCount: 3);

            SaveKeyStore();

            _logger.LogInformation("Key rotation completed. New KeyId: {KeyId}", newKey.KeyId);
            return newKey;
        }
        finally
        {
            _lock.Release();
        }
    }

    private RsaKeyInfo GenerateRsaKey(int keySize)
    {
        using var rsa = RSA.Create(keySize);
        
        var privateKeyPem = rsa.ExportRSAPrivateKeyPem();
        var publicKeyPem = rsa.ExportRSAPublicKeyPem();

        return new RsaKeyInfo
        {
            KeyId = Guid.NewGuid().ToString(),
            PrivateKey = privateKeyPem,
            PublicKey = publicKeyPem,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(60), // Key valid 60 ngày
            IsActive = true,
            KeySize = keySize
        };
    }

    private void CleanupOldKeys(int keepCount)
    {
        if (_keyStore.Keys.Count <= keepCount)
        {
            return;
        }
        
        var keysToRemove = _keyStore.Keys
            .OrderByDescending(k => k.CreatedAt)
            .Skip(keepCount)
            .Where(k => k.ExpiresAt < DateTime.UtcNow) // Chỉ xóa key đã hết hạn
            .ToList();

        foreach (var key in keysToRemove)
        {
            _keyStore.Keys.Remove(key);
            _logger.LogInformation("Removed expired key: {KeyId}", key.KeyId);
        }
    }

    public RsaSecurityKey GetCurrentPrivateKey()
    {
        if (_keyStore.CurrentKey == null)
            throw new InvalidOperationException("No active key available");

        var rsa = RSA.Create();
        rsa.ImportFromPem(_keyStore.CurrentKey.PrivateKey);
        return new RsaSecurityKey(rsa) { KeyId = _keyStore.CurrentKey.KeyId };
    }

    public RsaSecurityKey GetCurrentPublicKey()
    {
        if (_keyStore.CurrentKey == null)
            throw new InvalidOperationException("No active key available");

        var rsa = RSA.Create();
        rsa.ImportFromPem(_keyStore.CurrentKey.PublicKey);
        return new RsaSecurityKey(rsa) { KeyId = _keyStore.CurrentKey.KeyId };
    }

    public List<RsaSecurityKey> GetAllPublicKeys()
    {
        return _keyStore.Keys
            .Where(k => k.ExpiresAt > DateTime.UtcNow) // Chỉ lấy key chưa hết hạn
            .Select(keyInfo =>
            {
                var rsa = RSA.Create();
                rsa.ImportFromPem(keyInfo.PublicKey);
                return new RsaSecurityKey(rsa) { KeyId = keyInfo.KeyId };
            })
            .ToList();
    }

    public KeyStore GetKeyStore() => _keyStore;

    public string GetCurrentKeyId() => _keyStore.CurrentKey?.KeyId ?? string.Empty;
}
