using Microsoft.Extensions.Caching.Distributed;
using StackExchange.Redis;

namespace A2I.Infrastructure.Caching.Providers;

public sealed class RedisDistributedCache : IRedisDistributedCache, IAsyncDisposable
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IDatabase _db;
    private readonly int _defaultExpirationMinutes;
    private bool _disposed; 
    
    public RedisDistributedCache(IConnectionMultiplexer redis, CacheOptions options)
    {
        ArgumentNullException.ThrowIfNull(redis);   
        ArgumentNullException.ThrowIfNull(options);
        _redis = redis;
        _db = _redis.GetDatabase();
        _defaultExpirationMinutes = options.DefaultExpirationMinutes;
    }
    
    #region Hash operations
    
    public async Task SetHashAsync(string key, Dictionary<string, string> hash, DistributedCacheEntryOptions? options = null)
    {
        var entries = hash.Select(kv => new HashEntry(kv.Key, kv.Value)).ToArray();
        await _db.HashSetAsync(key, entries);
        
        await ApplyExpirationAsync(key, options);
    }
    
    public async Task<Dictionary<string, string>> GetHashAsync(string key)
    {
        var entries = await _db.HashGetAllAsync(key);
        return entries.ToDictionary(
            e => e.Name.ToString(), 
            e => e.Value.ToString()
        );
    }
    
    public async Task SetHashFieldAsync(string key, string field, string value, DistributedCacheEntryOptions? options = null)
    {
        await _db.HashSetAsync(key, field, value);
        await ApplyExpirationAsync(key, options);
    }
    
    public async Task<string?> GetHashFieldAsync(string key, string field)
    {
        return await _db.HashGetAsync(key, field);
    }
    
    #endregion
    
    #region List operations
    
    public async Task<long> ListPushAsync(string key, string value, DistributedCacheEntryOptions? options = null)
    {
        var result = await _db.ListRightPushAsync(key, value);
        await ApplyExpirationAsync(key, options);
        return result;
    }
    
    public async Task<string?> ListPopAsync(string key)
    {
        return await _db.ListRightPopAsync(key);
    }
    
    public async Task<string[]> ListRangeAsync(string key, long start = 0, long stop = -1)
    {
        var values = await _db.ListRangeAsync(key, start, stop);
        return values.Select(v => v.ToString()).ToArray();
    }
    
    public async Task<long> ListLengthAsync(string key)
    {
        return await _db.ListLengthAsync(key);
    }
    
    #endregion
    
    #region Set operations
    
    public async Task<bool> SetAddAsync(string key, string value, DistributedCacheEntryOptions? options = null)
    {
        var result = await _db.SetAddAsync(key, value);
        await ApplyExpirationAsync(key, options);
        return result;
    }
    
    public async Task<bool> SetRemoveAsync(string key, string value)
    {
        return await _db.SetRemoveAsync(key, value);
    }
    
    public async Task<string[]> SetMembersAsync(string key)
    {
        var members = await _db.SetMembersAsync(key);
        return members.Select(m => m.ToString()).ToArray();
    }
    
    public async Task<bool> SetContainsAsync(string key, string value)
    {
        return await _db.SetContainsAsync(key, value);
    }
    
    #endregion
    
    #region Sorted Set operations
    
    public async Task<bool> SortedSetAddAsync(string key, string member, double score, DistributedCacheEntryOptions? options = null)
    {
        var result = await _db.SortedSetAddAsync(key, member, score);
        await ApplyExpirationAsync(key, options);
        return result;
    }
    
    public async Task<SortedSetEntry[]> SortedSetRangeByRankAsync(string key, long start = 0, long stop = -1)
    {
        return await _db.SortedSetRangeByRankWithScoresAsync(key, start, stop);
    }
    
    public async Task<double?> SortedSetScoreAsync(string key, string member)
    {
        return await _db.SortedSetScoreAsync(key, member);
    }
    
    #endregion
    
    #region Increment/Decrement
    
    public async Task<long> IncrementAsync(string key, long value = 1, DistributedCacheEntryOptions? options = null)
    {
        var result = await _db.StringIncrementAsync(key, value);
        await ApplyExpirationAsync(key, options);
        return result;
    }
    
    public async Task<long> DecrementAsync(string key, long value = 1, DistributedCacheEntryOptions? options = null)
    {
        var result = await _db.StringDecrementAsync(key, value);
        await ApplyExpirationAsync(key, options);
        return result;
    }
    
    #endregion
    
    #region Key operations
    
    public async Task<bool> KeyExistsAsync(string key)
    {
        return await _db.KeyExistsAsync(key);
    }
    
    public async Task<bool> KeyDeleteAsync(string key)
    {
        return await _db.KeyDeleteAsync(key);
    }
    
    public async Task<bool> KeyExpireAsync(string key, TimeSpan expiry)
    {
        return await _db.KeyExpireAsync(key, expiry);
    }
    
    public async Task<TimeSpan?> KeyTimeToLiveAsync(string key)
    {
        return await _db.KeyTimeToLiveAsync(key);
    }
    
    #endregion
    
    #region Batch operations
    
    public async Task<bool> SetManyAsync(Dictionary<string, byte[]> items, DistributedCacheEntryOptions? options = null)
    {
        var batch = _db.CreateBatch();
        var tasks = new List<Task>();
        
        foreach (var item in items)
        {
            var expiry = GetExpiry(options);
            tasks.Add(batch.StringSetAsync(item.Key, item.Value, expiry));
        }
        
        batch.Execute();
        await Task.WhenAll(tasks);
        return true;
    }
    
    public async Task<Dictionary<string, byte[]>> GetManyAsync(string[] keys)
    {
        var redisKeys = keys.Select(k => (RedisKey)k).ToArray();
        var values = await _db.StringGetAsync(redisKeys);
        
        var result = new Dictionary<string, byte[]>();
        for (var i = 0; i < keys.Length; i++)
        {
            if (values[i].HasValue)
            {
                result[keys[i]] = values[i]!;
            }
        }
        
        return result;
    }
    
    #endregion
    
    #region Helper methods
    
    private async Task ApplyExpirationAsync(string key, DistributedCacheEntryOptions? options)
    {
        var expiry = GetExpiry(options);
        if (expiry.HasValue)
        {
            await _db.KeyExpireAsync(key, expiry.Value);
        }
        else if (_defaultExpirationMinutes > 0)
        {
            await _db.KeyExpireAsync(key, TimeSpan.FromMinutes(_defaultExpirationMinutes));
        }
    }
    
    private static TimeSpan? GetExpiry(DistributedCacheEntryOptions? options)
    {
        if (options == null) return null;
        
        if (options.AbsoluteExpirationRelativeToNow.HasValue)
            return options.AbsoluteExpirationRelativeToNow.Value;
        
        if (options.AbsoluteExpiration.HasValue)
            return options.AbsoluteExpiration.Value - DateTimeOffset.UtcNow;
        
        return options.SlidingExpiration;
    }
    
    #endregion

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        
        _disposed = true;
        await _redis.DisposeAsync();
    }
}