namespace A2I.Application.Common.Caching;

public interface ICacheService
{
    Task<CacheResult<T>> GetAsync<T>(string key);
    Task<bool> SetAsync<T>(string key, T value, TimeSpan? expiration = null);
    Task<bool> RemoveAsync(string key);
    Task<bool> ExistsAsync(string key);
    Task<bool> SetIfNotExistsAsync<T>(string key, T value, TimeSpan? expiration = null);
    Task<long> IncrementAsync(string key, long value = 1);
    Task<long> DecrementAsync(string key, long value = 1);
    Task<CacheResult<T>> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiration = null);
}