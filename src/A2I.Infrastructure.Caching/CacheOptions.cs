namespace A2I.Infrastructure.Caching;

public enum CacheType
{
    Redis,
    Memory
}

public class CacheOptions
{
    public CacheType CacheType { get; set; } = CacheType.Redis;
    public string? ConnectionString { get; set; }
    public int DefaultExpirationMinutes { get; set; } = 60;
    public string? InstanceName { get; set; }
}