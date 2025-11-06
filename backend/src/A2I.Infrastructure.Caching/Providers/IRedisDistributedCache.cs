using Microsoft.Extensions.Caching.Distributed;
using StackExchange.Redis;

namespace A2I.Infrastructure.Caching.Providers;

public interface IRedisDistributedCache
{
    // Hash operations
    Task SetHashAsync(string key, Dictionary<string, string> hash, DistributedCacheEntryOptions? options = null);
    Task<Dictionary<string, string>> GetHashAsync(string key);
    Task SetHashFieldAsync(string key, string field, string value, DistributedCacheEntryOptions? options = null);
    Task<string?> GetHashFieldAsync(string key, string field);
    
    // List operations
    Task<long> ListPushAsync(string key, string value, DistributedCacheEntryOptions? options = null);
    Task<string?> ListPopAsync(string key);
    Task<string[]> ListRangeAsync(string key, long start = 0, long stop = -1);
    Task<long> ListLengthAsync(string key);
    
    // Set operations
    Task<bool> SetAddAsync(string key, string value, DistributedCacheEntryOptions? options = null);
    Task<bool> SetRemoveAsync(string key, string value);
    Task<string[]> SetMembersAsync(string key);
    Task<bool> SetContainsAsync(string key, string value);
    
    // Sorted Set operations
    Task<bool> SortedSetAddAsync(string key, string member, double score, DistributedCacheEntryOptions? options = null);
    Task<SortedSetEntry[]> SortedSetRangeByRankAsync(string key, long start = 0, long stop = -1);
    Task<double?> SortedSetScoreAsync(string key, string member);
    
    // Increment/Decrement
    Task<long> IncrementAsync(string key, long value = 1, DistributedCacheEntryOptions? options = null);
    Task<long> DecrementAsync(string key, long value = 1, DistributedCacheEntryOptions? options = null);
    
    // Key operations
    Task<bool> KeyExistsAsync(string key);
    Task<bool> KeyDeleteAsync(string key);
    Task<bool> KeyExpireAsync(string key, TimeSpan expiry);
    Task<TimeSpan?> KeyTimeToLiveAsync(string key);
    
    // Batch operations
    Task<bool> SetManyAsync(Dictionary<string, byte[]> items, DistributedCacheEntryOptions? options = null);
    Task<Dictionary<string, byte[]>> GetManyAsync(string[] keys);
}