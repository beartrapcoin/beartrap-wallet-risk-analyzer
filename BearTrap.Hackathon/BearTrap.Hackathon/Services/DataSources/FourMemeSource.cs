using BearTrap.Hackathon.Application.Abstractions;
using BearTrap.Hackathon.Domain;
using Microsoft.Extensions.Caching.Memory;

namespace BearTrap.Hackathon.Services.DataSources;

/// <summary>
/// Adapter that implements IFourMemeMainListSource using the Four.Meme API client.
/// Provides access to the main list of Four.Meme tokens.
/// </summary>
public sealed class FourMemeSource : IFourMemeMainListSource
{
    private readonly IFourMemeClient _fourMemeClient;
    private readonly IMemoryCache _memoryCache;

    private static readonly TimeSpan SearchCacheTtl = TimeSpan.FromMinutes(7);

    public FourMemeSource(IFourMemeClient fourMemeClient, IMemoryCache memoryCache)
    {
        _fourMemeClient = fourMemeClient ?? throw new ArgumentNullException(nameof(fourMemeClient));
        _memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
    }

    public async Task<bool> IsFourMemeLaunchAsync(string tokenAddress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(tokenAddress))
            return false;

        return await _fourMemeClient.IsTokenOnMainListAsync(tokenAddress, ct);
    }

    public async Task<IReadOnlyList<FourMemeListedToken>> GetMainListAsync(int pageSize, CancellationToken ct)
    {
        return await _fourMemeClient.GetMainListAsync(pageSize, ct);
    }

    public async Task<IReadOnlyList<TokenDto>> QueryTokensAsync(string? tokenName, int pageIndex, int pageSize, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var normalizedQuery = (tokenName ?? string.Empty).Trim();
        var isSearchMode = !string.IsNullOrWhiteSpace(normalizedQuery);

        var safePageIndex = Math.Max(1, pageIndex);
        var safePageSize = isSearchMode
            ? Math.Clamp(pageSize, 1, 20)
            : Math.Max(1, pageSize);

        if (isSearchMode)
        {
            var cacheKey = $"fourmeme:query:{normalizedQuery.ToLowerInvariant()}:{safePageIndex}:{safePageSize}";
            if (_memoryCache.TryGetValue(cacheKey, out IReadOnlyList<TokenDto>? cached) && cached is not null)
            {
                return cached;
            }

            var queriedTokens = await _fourMemeClient.QueryTokensAsync(normalizedQuery, safePageIndex, safePageSize, ct);
            var queriedDtos = queriedTokens.Select(MapToDto).ToList();

            _memoryCache.Set(cacheKey, queriedDtos, SearchCacheTtl);
            return queriedDtos;
        }

        var latestTokens = await _fourMemeClient.QueryTokensAsync(null, safePageIndex, safePageSize, ct);
        return latestTokens.Select(MapToDto).ToList();
    }

    private static TokenDto MapToDto(LatestToken token)
        => new(
            token.Address,
            token.Name,
            token.Symbol,
            token.Creator,
            token.CreatedAt,
            token.ImageUrl);
}
