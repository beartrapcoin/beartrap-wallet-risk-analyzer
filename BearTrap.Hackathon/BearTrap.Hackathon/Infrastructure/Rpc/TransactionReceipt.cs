namespace BearTrap.Hackathon.Infrastructure.Rpc;

/// <summary>
/// Represents a blockchain transaction receipt.
/// </summary>
public sealed class TransactionReceipt
{
    /// <summary>
    /// The transaction hash.
    /// </summary>
    public string TransactionHash { get; set; } = string.Empty;

    /// <summary>
    /// The index of the transaction in the block.
    /// </summary>
    public long TransactionIndex { get; set; }

    /// <summary>
    /// The block hash where the transaction was mined.
    /// </summary>
    public string BlockHash { get; set; } = string.Empty;

    /// <summary>
    /// The block number where the transaction was mined.
    /// </summary>
    public long BlockNumber { get; set; }

    /// <summary>
    /// The address of the contract created by the transaction (if it was a contract creation).
    /// </summary>
    public string? ContractAddress { get; set; }

    /// <summary>
    /// The total amount of gas used by the transaction.
    /// </summary>
    public long GasUsed { get; set; }

    /// <summary>
    /// The gas limit provided by the transaction.
    /// </summary>
    public long GasLimit { get; set; }

    /// <summary>
    /// The address that sent the transaction.
    /// </summary>
    public string From { get; set; } = string.Empty;

    /// <summary>
    /// The address the transaction was sent to (or null for contract creation).
    /// </summary>
    public string? To { get; set; }

    /// <summary>
    /// The transaction status (1 for success, 0 for failure).
    /// </summary>
    public long Status { get; set; }

    /// <summary>
    /// The cumulative gas used in the block up to this transaction.
    /// </summary>
    public long CumulativeGasUsed { get; set; }

    /// <summary>
    /// Logs emitted by the transaction.
    /// </summary>
    public IReadOnlyList<LogEvent> Logs { get; set; } = Array.Empty<LogEvent>();

    /// <summary>
    /// The transaction fee paid (gasUsed * gasPrice).
    /// </summary>
    public string TransactionFee { get; set; } = string.Empty;

    /// <summary>
    /// Whether the transaction was successful.
    /// </summary>
    public bool IsSuccessful => Status == 1;
}
