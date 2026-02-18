using BearTrap.Hackathon.Application.Abstractions;
using BearTrap.Hackathon.Domain;

namespace BearTrap.Hackathon.Services.DataSources;

/// <summary>
/// Adapter that implements IFourMemeSource using Four.Meme API as the primary data source.
/// Queries Four.Meme API first to discover latest tokens, minimizing RPC load.
/// Only uses IChainDataSource when additional on-chain verification is explicitly needed.
/// </summary>
public sealed class BitqueryFourMemeSource : IFourMemeSource
{
    private readonly IChainDataSource _chainDataSource;
    private readonly IFourMemeClient _fourMemeClient;

    public BitqueryFourMemeSource(
        IChainDataSource chainDataSource,
        IFourMemeClient fourMemeClient)
    {
        _chainDataSource = chainDataSource ?? throw new ArgumentNullException(nameof(chainDataSource));
        _fourMemeClient = fourMemeClient ?? throw new ArgumentNullException(nameof(fourMemeClient));
    }

    public async Task<bool> IsFourMemeLaunchAsync(string tokenAddress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(tokenAddress))
            return false;

        // Query Four.Meme API directly instead of scanning blockchain
        // This avoids expensive RPC calls and respects rate limits
        return await _fourMemeClient.IsTokenOnMainListAsync(tokenAddress, ct);
    }

    public async Task<IReadOnlyList<LatestToken>> GetLatestAsync(int count, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        
        // NEW BEHAVIOR: Fetch latest tokens from Four.Meme API FIRST
        // This discovers tokens via their API, avoiding broad blockchain scans
        // The API already provides: Address, Name, Symbol, Creator, CreatedAt, ImageUrl
        var latest = await _fourMemeClient.GetLatestTokensAsync(count, ct);
        
        // Convert IEnumerable to IReadOnlyList to match interface
        return latest.ToList();
    }
}
