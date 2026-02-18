using BearTrap.Hackathon.Domain;

namespace BearTrap.Hackathon.Application.Abstractions;

/// <summary>
/// Client for Bitquery GraphQL API integration.
/// Handles all Bitquery-specific requests and response mapping.
/// </summary>
public interface IBitqueryClient
{
    /// <summary>
    /// Fetches the latest token events from Bitquery that were launched via Four.Meme,
    /// filtered to only include tokens on the main Four.Meme list.
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
