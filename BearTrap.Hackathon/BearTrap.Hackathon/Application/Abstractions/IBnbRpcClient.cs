using BearTrap.Hackathon.Infrastructure.Rpc;

namespace BearTrap.Hackathon.Application.Abstractions;

/// <summary>
/// Client for Binance Smart Chain RPC provider integration.
/// Provides low-level access to blockchain data and events.
/// </summary>
public interface IBnbRpcClient
{
    /// <summary>
    /// Fetches the current block number on the BSC network.
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The latest block number</returns>
    Task<long> GetBlockNumberAsync(CancellationToken ct);

    /// <summary>
    /// Fetches logs matching the specified filter criteria.
    /// Used to query contract events like token transfers or approvals.
    /// </summary>
    /// <param name="filter">The log filter criteria</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of matching log events</returns>
    Task<IReadOnlyList<LogEvent>> GetLogsAsync(LogFilter filter, CancellationToken ct);

    /// <summary>
    /// Fetches the receipt of a confirmed transaction.
    /// </summary>
    /// <param name="txHash">The transaction hash to query</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The transaction receipt, or null if not found</returns>
    Task<TransactionReceipt?> GetReceiptAsync(string txHash, CancellationToken ct);
}
