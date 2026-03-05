namespace BearTrap.Hackathon.Application.Abstractions;

/// <summary>
/// Client for querying DexScreener paid visibility data.
/// </summary>
public interface IDexScreenerClient
{
    /// <summary>
    /// Returns true when DexScreener reports a paid boost or approved paid profile.
    /// Returns false on empty response or any API failure.
    /// </summary>
    Task<bool> HasPaidVisibilityAsync(string chainId, string tokenAddress, CancellationToken ct);
}
