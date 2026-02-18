namespace BearTrap.Hackathon.Application.Abstractions;

/// <summary>
/// Provides caching for chain data queries with optional request coalescing.
/// Supports TTL-based expiration and automatic cache invalidation.
/// </summary>
public interface IChainDataCache
{
    /// <summary>
    /// Gets cached value or creates it using the provided factory if not present or expired.
    /// Implements request coalescing: multiple concurrent requests for same key execute factory only once.
    /// </summary>
    /// <typeparam name="T">Type of cached value</typeparam>
    /// <param name="key">Cache key (should include all relevant filter parameters)</param>
    /// <param name="ttl">Time-to-live for the cached value</param>
    /// <param name="factory">Async factory to create the value if cache miss</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Cached or freshly created value</returns>
    Task<T> GetOrCreateAsync<T>(
        string key,
        TimeSpan ttl,
        Func<Task<T>> factory,
        CancellationToken ct)
        where T : class;

    /// <summary>
    /// Removes a cached value by key.
    /// </summary>
    /// <param name="key">Cache key to remove</param>
    void Remove(string key);

    /// <summary>
    /// Clears all cached values.
    /// </summary>
    void Clear();
}
