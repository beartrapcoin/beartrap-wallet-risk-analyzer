namespace BearTrap.Hackathon.Infrastructure.Rpc;

/// <summary>
/// Represents a blockchain log event from contract interactions.
/// </summary>
public sealed class LogEvent
{
    /// <summary>
    /// The address of the contract that emitted the log.
    /// </summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>
    /// Array of topics (indexed log parameters).
    /// </summary>
    public IReadOnlyList<string> Topics { get; set; } = Array.Empty<string>();

    /// <summary>
    /// The data field (non-indexed log parameters).
    /// </summary>
    public string Data { get; set; } = string.Empty;

    /// <summary>
    /// The block number where this log was emitted.
    /// </summary>
    public long BlockNumber { get; set; }

    /// <summary>
    /// The transaction hash that generated this log.
    /// </summary>
    public string TransactionHash { get; set; } = string.Empty;

    /// <summary>
    /// The index of this log within the transaction.
    /// </summary>
    public long LogIndex { get; set; }

    /// <summary>
    /// The block hash.
    /// </summary>
    public string BlockHash { get; set; } = string.Empty;

    /// <summary>
    /// The transaction index within the block.
    /// </summary>
    public long TransactionIndex { get; set; }
}
