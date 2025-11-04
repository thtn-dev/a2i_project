namespace A2I.Infrastructure.Identity.Security.Models;

public class RsaKeyInfo
{
    public string KeyId { get; set; } = Guid.NewGuid().ToString();
    public string PrivateKey { get; set; } = string.Empty;
    public string PublicKey { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public bool IsActive { get; set; } = true;
    public int KeySize { get; set; } = 2048;
}