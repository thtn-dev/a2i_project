using A2I.Application.Common.Caching;
using A2I.Infrastructure.Caching.Providers;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace A2I.Infrastructure.Caching;

public class CacheServiceFactory
{
    public static ICacheService Create(CacheOptions options, ILogger<RedisCacheService>? logger = null)
    {
        return options.CacheType switch
        {
            CacheType.Redis => CreateRedisCache(options, logger),
            CacheType.Memory => throw new NotImplementedException("Memory not implement"),
            _ => throw new ArgumentException($"Invalid cache type: {options.CacheType}")
        };
    }

    private static RedisCacheService CreateRedisCache(
        CacheOptions options, 
        ILogger<RedisCacheService>? logger)
    {
        if (string.IsNullOrEmpty(options.ConnectionString))
            throw new ArgumentException("Redis connection string is required");

        var redis = ConnectionMultiplexer.Connect(options.ConnectionString);
        return new RedisCacheService(redis, options, logger);
    }
}