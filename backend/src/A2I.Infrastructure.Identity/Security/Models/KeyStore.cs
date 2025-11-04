namespace A2I.Infrastructure.Identity.Security.Models;

public class KeyStore
{
    public RsaKeyInfo? CurrentKey { get; set; }
    public List<RsaKeyInfo> Keys { get; set; } = [];
    public DateTime LastRotation { get; set; }
}
