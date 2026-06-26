using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace CodeSage.Api.Services;

// Phase 4 #1: thin get-or-set over IDistributedCache.
// Backed by Redis when configured, otherwise an in-process memory cache — same API either way.
public class CacheService
{
    private readonly IDistributedCache _cache;
    public CacheService(IDistributedCache cache) => _cache = cache;

    public async Task<T> GetOrSetAsync<T>(string key, TimeSpan ttl, Func<Task<T>> factory)
    {
        try
        {
            var cached = await _cache.GetStringAsync(key);
            if (cached is not null) return JsonSerializer.Deserialize<T>(cached)!;
        }
        catch { /* cache miss or backend down -> fall through to factory */ }

        var value = await factory();

        try
        {
            await _cache.SetStringAsync(key, JsonSerializer.Serialize(value),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl });
        }
        catch { /* never let a cache write break the request */ }

        return value;
    }

    public Task RemoveAsync(string key) => _cache.RemoveAsync(key);
}