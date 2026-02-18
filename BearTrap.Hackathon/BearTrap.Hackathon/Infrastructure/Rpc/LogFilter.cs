namespace BearTrap.Hackathon.Infrastructure.Rpc;

/// <summary>
/// Represents a filter for querying blockchain logs.
/// </summary>
public sealed class LogFilter
{
    /// <summary>
    /// Starting block number (or block tag like "latest").
    /// </summary>
    public string? FromBlock { get; set; }

    /// <summary>
    /// Ending block number (or block tag like "latest").
    /// </summary>
    public string? ToBlock { get; set; }

    /// <summary>
    /// Contract addresses to filter logs from.
    /// </summary>
    public IReadOnlyList<string> Addresses { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Topics to filter. Can use nested arrays for OR logic.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<string>> Topics { get; set; } = Array.Empty<IReadOnlyList<string>>();

    /// <summary>
    /// Limits the number of logs returned.
    /// </summary>
    public int? Limit { get; set; }
}
