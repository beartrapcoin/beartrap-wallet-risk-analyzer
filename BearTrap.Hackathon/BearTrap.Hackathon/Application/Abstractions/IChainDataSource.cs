using BearTrap.Hackathon.Domain;

namespace BearTrap.Hackathon.Application.Abstractions;

/// <summary>
/// Abstracts chain data access, allowing different providers (Bitquery, RPC, etc).
/// Provides token event information and Four.Meme verification.
/// </summary>
public interface IChainDataSource
{
    /// <summary>
    /// Fetches the latest tokens from the chain data provider.
    /// </summary>
    /// <param name="count">Maximum number of tokens to retrieve</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of latest tokens with their metadata</returns>
    Task<IReadOnlyList<LatestToken>> GetLatestTokensAsync(int count, CancellationToken ct);

    /// <summary>
    /// Checks if a given token address was launched via Four.Meme.
    /// </summary>
    /// <param name="tokenAddress">The token address to check</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>True if token is a Four.Meme launch, false otherwise</returns>
    Task<bool> IsTokenFromFourMemeAsync(string tokenAddress, CancellationToken ct);
}
