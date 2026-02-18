using BearTrap.Hackathon.Application.Abstractions;
using Microsoft.Extensions.Caching.Memory;
using System.Collections.Concurrent;

namespace BearTrap.Hackathon.Infrastructure.Caching;

/// <summary>
/// In-memory implementation of IChainDataCache with request coalescing.
/// Prevents cache stampede by executing factory only once for concurrent identical requests.
/// </summary>
public sealed class MemoryChainDataCache : IChainDataCache
{
    private readonly IMemoryCache _memoryCache;
    private readonly ConcurrentDictionary<string, Lazy<Task<object>>> _inFlightRequests;

    public MemoryChainDataCache(IMemoryCache memoryCache)
    {
        _memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
        _inFlightRequests = new ConcurrentDictionary<string, Lazy<Task<object>>>();
    }

    public async Task<T> GetOrCreateAsync<T>(
        string key,
        TimeSpan ttl,
        Func<Task<T>> factory,
        CancellationToken ct)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Cache key cannot be null or empty.", nameof(key));
        
        if (factory == null)
            throw new ArgumentNullException(nameof(factory));

        ct.ThrowIfCancellationRequested();

        // Try to get from cache first
        if (_memoryCache.TryGetValue(key, out T? cachedValue))
        {
            return cachedValue!;
        }

        // Request coalescing: if multiple concurrent requests for same key,
        // only execute factory once, other requests wait for same result
        var lazyTask = _inFlightRequests.GetOrAdd(
            key,
            _ => new Lazy<Task<object>>(async () =>
            {
                try
                {
                    var result = await factory().ConfigureAwait(false);
                    if (result != null)
                    {
                        _memoryCache.Set(key, result, ttl);
                    }
                    return (object?)result;
                }
                finally
                {
                    _inFlightRequests.TryRemove(key, out var _);
                }
            }, isThreadSafe: true));

        try
        {
            var result = await lazyTask.Value.ConfigureAwait(false);
            return (T?)result ?? throw new InvalidOperationException($"Factory returned null for cache key: {key}");
        }
        catch (Exception)
        {
            // Remove from in-flight if factory threw exception
            // Cache does NOT store exceptions
            _inFlightRequests.TryRemove(key, out var _);
            throw;
        }
    }

    public void Remove(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return;

        _memoryCache.Remove(key);
        _inFlightRequests.TryRemove(key, out _);
    }

    public void Clear()
    {
        if (_memoryCache is MemoryCache memCache)
        {
            memCache.Compact(1.0);
        }
        _inFlightRequests.Clear();
    }
}
