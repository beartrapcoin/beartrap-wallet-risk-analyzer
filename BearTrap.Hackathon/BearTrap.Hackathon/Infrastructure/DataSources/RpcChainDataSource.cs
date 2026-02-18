using BearTrap.Hackathon.Application.Abstractions;
using BearTrap.Hackathon.Domain;
using BearTrap.Hackathon.Infrastructure.Caching;
using BearTrap.Hackathon.Infrastructure.Rpc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BearTrap.Hackathon.Infrastructure.DataSources;

/// <summary>
/// Implements IChainDataSource using Binance Smart Chain RPC as the data provider.
/// Routes all chain data requests to the RPC node, querying logs and events directly.
/// Caches high-frequency queries for 60-120 seconds with request coalescing.
/// </summary>
public sealed class RpcChainDataSource : IChainDataSource
{
    private readonly IBnbRpcClient _bnbRpcClient;
    private readonly IChainDataCache _cache;
    private readonly ILogger<RpcChainDataSource> _logger;
    private readonly string _fourMemeFactoryAddress;
    
    // Event signature for TokenCreate (topic 0)
    // TokenCreate(address indexed token, address indexed creator)
    private const string TokenCreateTopic0 = "0x0d3648bd0f6ba80134a33ba9275ac585d9d315f0ad8355cddefde31afa28d0e9";
    
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(90);

    public RpcChainDataSource(
        IBnbRpcClient bnbRpcClient,
        IChainDataCache cache,
        IConfiguration configuration,
        ILogger<RpcChainDataSource> logger)
    {
        _bnbRpcClient = bnbRpcClient ?? throw new ArgumentNullException(nameof(bnbRpcClient));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        if (configuration == null) throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _fourMemeFactoryAddress = (configuration["FourMeme:FactoryAddress"] ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(_fourMemeFactoryAddress) &&
            !_fourMemeFactoryAddress.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            _fourMemeFactoryAddress = "0x" + _fourMemeFactoryAddress;
        }

        _fourMemeFactoryAddress = _fourMemeFactoryAddress.ToLowerInvariant();
    }

    public async Task<IReadOnlyList<LatestToken>> GetLatestTokensAsync(int count, CancellationToken ct)
    {
        EnsureConfigured();
        ct.ThrowIfCancellationRequested();
        if (count <= 0)
            return Array.Empty<LatestToken>();

        // Use deterministic cache key including count parameter
        var cacheKey = $"rpc:latest_tokens:{count}";

        return await _cache.GetOrCreateAsync<IReadOnlyList<LatestToken>>(
            cacheKey,
            CacheTtl,
            async () =>
            {
                _logger.LogInformation("Fetching latest {Count} tokens from RPC", count);
                
                // Query TokenCreate events from factory contract
                // IMPORTANT: Limit to last 3000 blocks to avoid RPC limit exceeded errors
                var currentBlock = await _bnbRpcClient.GetBlockNumberAsync(ct);
                var fromBlock = Math.Max(0, currentBlock - 3000);

                var filter = new LogFilter
                {
                    FromBlock = $"0x{fromBlock:x}",
                    ToBlock = "latest",
                    Addresses = new[] { _fourMemeFactoryAddress },
                    Topics = new[] { new[] { TokenCreateTopic0 }.AsReadOnly() }
                };

                var logs = await _bnbRpcClient.GetLogsAsync(filter, ct);
                
                // Parse TokenCreate events and convert to LatestToken
                var tokens = new List<LatestToken>();
                
                foreach (var log in logs.TakeLast(count))
                {
                    try
                    {
                        var token = ParseTokenCreateEvent(log, currentBlock);
                        if (token != null)
                            tokens.Add(token);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to parse TokenCreate event from log");
                    }
                }

                tokens.Reverse(); // Most recent first
                _logger.LogInformation("Parsed {TokenCount} tokens from {LogCount} logs", tokens.Count, logs.Count);
                
                return tokens.AsReadOnly();
            },
            ct);
    }

    public async Task<bool> IsTokenFromFourMemeAsync(string tokenAddress, CancellationToken ct)
    {
        EnsureConfigured();
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(tokenAddress))
            return false;

        // Use deterministic cache key including token address
        var normalizedAddress = tokenAddress.Trim().ToLowerInvariant();
        var cacheKey = $"rpc:is_fourmeme:{normalizedAddress}";

        return (await _cache.GetOrCreateAsync<CachedBool>(
            cacheKey,
            CacheTtl,
            async () =>
            {
                _logger.LogDebug("Verifying if token {TokenAddress} is from Four.Meme", normalizedAddress);
                
                // Query for TokenCreate events from factory with ADDRESS-SCOPED filter
                // Filter by specific token address (topics[1]) to avoid broad scans
                // TokenCreate(address indexed token, address indexed creator)
                // - topics[0] = event signature
                // - topics[1] = token address (indexed parameter)
                var currentBlock = await _bnbRpcClient.GetBlockNumberAsync(ct);
                var fromBlock = Math.Max(0, currentBlock - 3000);

                // Pad token address to 32 bytes (64 hex chars) for topic filter
                var tokenAddressForTopic = normalizedAddress.StartsWith("0x") 
                    ? "0x" + normalizedAddress[2..].PadLeft(64, '0')
                    : "0x" + normalizedAddress.PadLeft(64, '0');

                var filter = new LogFilter
                {
                    FromBlock = $"0x{fromBlock:x}",
                    ToBlock = "latest",
                    Addresses = new[] { _fourMemeFactoryAddress },
                    // Add token address as topics[1] to filter by specific token only
                    Topics = new[] 
                    { 
                        new[] { TokenCreateTopic0 }.AsReadOnly(),
                        new[] { tokenAddressForTopic }.AsReadOnly()
                    }
                };

                var logs = await _bnbRpcClient.GetLogsAsync(filter, ct);

                // If any logs match, token is from Four.Meme factory
                bool isFourMeme = logs.Count > 0;

                _logger.LogDebug("Token {TokenAddress} is Four.Meme: {IsFourMeme}", normalizedAddress, isFourMeme);
                return new CachedBool { Value = isFourMeme };
            },
            ct)).Value;
    }

    /// <summary>
    /// Parses a TokenCreate event log into a LatestToken domain object.
    /// TokenCreate(address indexed token, address indexed creator)
    /// </summary>
    private static LatestToken? ParseTokenCreateEvent(LogEvent log, long currentBlock)
    {
        try
        {
            if (log.Topics.Count < 2)
                return null;

            // Extract token address from topics[1]
            var tokenAddress = NormalizeAddress(log.Topics[1]);
            
            // Extract creator from topics[2] if available
            string? creator = null;
            if (log.Topics.Count >= 3)
                creator = NormalizeAddress(log.Topics[2]);

            // Estimate creation time based on block number
            // BlockNumber is in log, estimate CreatedAt as ~3 seconds per block on BSC
            var blockTimeEstimate = DateTimeOffset.UtcNow.AddSeconds(-(currentBlock - log.BlockNumber) * 3);

            return new LatestToken(
                Address: tokenAddress,
                Name: $"Token {tokenAddress[..10]}",
                Symbol: "???",
                Creator: creator ?? string.Empty,
                CreatedAt: blockTimeEstimate,
                ImageUrl: null
            );
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Normalizes Ethereum address format (ensures 0x prefix and lowercase).
    /// </summary>
    private static string NormalizeAddress(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
            return string.Empty;

        var trimmed = address.Trim();
        
        // Remove 0x prefix temporarily to handle padding
        var hexPart = trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? trimmed[2..]
            : trimmed;

        // Pad to 40 characters if needed (20 bytes = 40 hex chars)
        if (hexPart.Length < 40)
            hexPart = hexPart.PadLeft(40, '0');

        return "0x" + hexPart.ToLowerInvariant();
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_fourMemeFactoryAddress))
        {
            throw new InvalidOperationException(
                "RPC provider is not configured. Missing 'FourMeme:FactoryAddress'. Configure it via ASP.NET Core User Secrets (Development) or environment variables.");
        }
    }

    /// <summary>
    /// Helper class to cache boolean primitives as objects.
    /// </summary>
    private sealed class CachedBool
    {
        public bool Value { get; set; }
    }
}
