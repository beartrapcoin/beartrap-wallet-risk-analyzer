using BearTrap.Hackathon.Application.Abstractions;
using BearTrap.Hackathon.Domain;
using BearTrap.Hackathon.Infrastructure.Caching;

namespace BearTrap.Hackathon.Infrastructure.DataSources;

/// <summary>
/// Implements IChainDataSource using Bitquery as the data provider.
/// Routes all chain data requests to the Bitquery GraphQL API.
/// Caches high-frequency queries for 60-120 seconds with request coalescing.
/// </summary>
public sealed class BitqueryChainDataSource : IChainDataSource
{
    private readonly IBitqueryClient _bitqueryClient;
    private readonly IChainDataCache _cache;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(90);

    public BitqueryChainDataSource(IBitqueryClient bitqueryClient, IChainDataCache cache)
    {
        _bitqueryClient = bitqueryClient ?? throw new ArgumentNullException(nameof(bitqueryClient));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    public async Task<IReadOnlyList<LatestToken>> GetLatestTokensAsync(int count, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        
        // Use deterministic cache key including count parameter
        var cacheKey = $"bitquery:latest_tokens:{count}";
        
        return await _cache.GetOrCreateAsync(
            cacheKey,
            CacheTtl,
            () => _bitqueryClient.GetLatestTokensAsync(count, ct),
            ct);
    }

    public async Task<bool> IsTokenFromFourMemeAsync(string tokenAddress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(tokenAddress))
            return false;
        
        // Use deterministic cache key including token address
        var cacheKey = $"bitquery:is_fourmeme:{tokenAddress.ToLowerInvariant()}";
        
        return await _cache.GetOrCreateAsync(
            cacheKey,
            CacheTtl,
            async () =>
            {
                var result = await _bitqueryClient.IsTokenFromFourMemeAsync(tokenAddress, ct);
                // Box boolean result
                return new CachedBool { Value = result };
            },
            ct).ContinueWith(task => task.Result.Value, ct);
    }

    /// <summary>
    /// Helper class to cache boolean primitives as objects.
    /// </summary>
    private sealed class CachedBool
    {
        public bool Value { get; set; }
    }
}
