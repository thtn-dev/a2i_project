using System.Text.Json;
using A2I.Application.Common.Caching;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace A2I.Infrastructure.Caching.Providers;

public class RedisCacheService : ICacheService
    {
        private readonly IDatabase _db;
        private readonly CacheOptions _options;
        private readonly ILogger<RedisCacheService>? _logger;

        public RedisCacheService(
            IConnectionMultiplexer redis, 
            CacheOptions options,
            ILogger<RedisCacheService>? logger = null)
        {
            var redis1 = redis ?? throw new ArgumentNullException(nameof(redis));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _db = redis1.GetDatabase();
            _logger = logger;
        }

        public async Task<CacheResult<T>> GetAsync<T>(string key)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentNullException(nameof(key));

            try
            {
                var value = await _db.StringGetAsync(GetFullKey(key));
                
                if (!value.HasValue)
                {
                    _logger?.LogDebug("Cache miss for key: {Key}", key);
                    return CacheResult<T>.Miss();
                }

                var deserializedValue = JsonSerializer.Deserialize<T>(value!);
                _logger?.LogDebug("Cache hit for key: {Key}", key);
                return CacheResult<T>.Hit(deserializedValue!);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting cache for key: {Key}", key);
                return CacheResult<T>.Error($"Failed to get cache: {ex.Message}");
            }
        }

        public async Task<CacheResult<T>> GetOrSetAsync<T>(
            string key, 
            Func<Task<T>> factory, 
            TimeSpan? expiration = null)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentNullException(nameof(key));
            
            if (factory == null)
                throw new ArgumentNullException(nameof(factory));

            var cacheResult = await GetAsync<T>(key);
            
            if (cacheResult.IsHit)
                return cacheResult;

            try
            {
                var value = await factory();
                await SetAsync(key, value, expiration);
                _logger?.LogDebug("Cache set for key: {Key}", key);
                return CacheResult<T>.Hit(value);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error in GetOrSet factory for key: {Key}", key);
                return CacheResult<T>.Error($"Failed to execute factory: {ex.Message}");
            }
        }

        public async Task<bool> SetAsync<T>(string key, T value, TimeSpan? expiration = null)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentNullException(nameof(key));

            try
            {
                var serializedValue = JsonSerializer.Serialize(value);
                var expiryTime = expiration ?? TimeSpan.FromMinutes(_options.DefaultExpirationMinutes);

                var result = await _db.StringSetAsync(GetFullKey(key), serializedValue, expiryTime);
                _logger?.LogDebug("Cache set for key: {Key}, Success: {Success}", key, result);
                return result;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error setting cache for key: {Key}", key);
                return false;
            }
        }

        public async Task<bool> RemoveAsync(string key)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentNullException(nameof(key));

            try
            {
                var result = await _db.KeyDeleteAsync(GetFullKey(key));
                _logger?.LogDebug("Cache remove for key: {Key}, Success: {Success}", key, result);
                return result;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error removing cache for key: {Key}", key);
                return false;
            }
        }

        public async Task<bool> ExistsAsync(string key)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentNullException(nameof(key));

            try
            {
                return await _db.KeyExistsAsync(GetFullKey(key));
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error checking cache exists for key: {Key}", key);
                return false;
            }
        }

        public async Task<bool> SetIfNotExistsAsync<T>(string key, T value, TimeSpan? expiration = null)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentNullException(nameof(key));

            try
            {
                var serializedValue = JsonSerializer.Serialize(value);
                var expiryTime = expiration ?? TimeSpan.FromMinutes(_options.DefaultExpirationMinutes);

                var result = await _db.StringSetAsync(
                    GetFullKey(key), 
                    serializedValue, 
                    expiryTime, 
                    When.NotExists
                );
                _logger?.LogDebug("Cache SetIfNotExists for key: {Key}, Success: {Success}", key, result);
                return result;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error in SetIfNotExists for key: {Key}", key);
                return false;
            }
        }

        public async Task<long> IncrementAsync(string key, long value = 1)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentNullException(nameof(key));

            try
            {
                return await _db.StringIncrementAsync(GetFullKey(key), value);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error incrementing cache for key: {Key}", key);
                throw;
            }
        }

        public async Task<long> DecrementAsync(string key, long value = 1)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentNullException(nameof(key));

            try
            {
                return await _db.StringDecrementAsync(GetFullKey(key), value);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error decrementing cache for key: {Key}", key);
                throw;
            }
        }

        private string GetFullKey(string key)
        {
            return string.IsNullOrEmpty(_options.InstanceName) 
                ? key 
                : $"{_options.InstanceName}:{key}";
        }
    }