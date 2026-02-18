using BearTrap.Hackathon.Domain;

namespace BearTrap.Hackathon.Application.Abstractions;

/// <summary>
/// Client for Four.Meme REST API integration.
/// Handles all Four.Meme-specific requests and response mapping.
/// </summary>
public interface IFourMemeClient
{
    /// <summary>
    /// Fetches the latest tokens from the Four.Meme main list.
    /// </summary>
    /// <param name="count">Maximum number of tokens to retrieve</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of latest tokens with their metadata</returns>
    Task<IEnumerable<LatestToken>> GetLatestTokensAsync(int count, CancellationToken ct);

    /// <summary>
    /// Fetches the main list of Four.Meme tokens with pagination support.
    /// </summary>
    /// <param name="pageSize">Number of tokens per page</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of Four.Meme listed tokens</returns>
    Task<IReadOnlyList<FourMemeListedToken>> GetMainListAsync(int pageSize, CancellationToken ct);

    /// <summary>
    /// Checks if a given token address is on the Four.Meme main list.
    /// </summary>
    /// <param name="tokenAddress">The token address to check</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>True if token is on the main list, false otherwise</returns>
    Task<bool> IsTokenOnMainListAsync(string tokenAddress, CancellationToken ct);

    /// <summary>
    /// Fetches the addresses of all tokens on the Four.Meme main list matching the filter criteria.
    /// </summary>
    /// <param name="orderBy">Sort order (e.g., "TimeDesc")</param>
    /// <param name="listedPancake">Filter by Pancake listing status</param>
    /// <param name="pageIndex">Page index for pagination</param>
    /// <param name="pageSize">Number of tokens per page</param>
    /// <param name="labels">Optional label filter</param>
    /// <param name="tokenName">Optional token name filter</param>
    /// <param name="symbol">Optional symbol filter</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of token addresses matching the filters</returns>
    Task<IReadOnlyList<string>> GetMainListAddressesAsync(
        string orderBy,
        bool listedPancake,
        int pageIndex,
        int pageSize,
        string? labels,
        string? tokenName,
        string? symbol,
        CancellationToken ct);
}
