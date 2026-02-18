using BearTrap.Hackathon.Application.Abstractions;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BearTrap.Hackathon.Infrastructure.Rpc;

/// <summary>
/// Implements BNB Smart Chain JSON-RPC 2.0 client for direct blockchain queries.
/// Communicates with BSC nodes via standard JSON-RPC HTTP POST interface.
/// Handles hex/decimal conversions and event log mapping.
/// </summary>
public sealed class BnbRpcClient : IBnbRpcClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<BnbRpcClient> _logger;
    private readonly string? _configurationError;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    private static int _requestId = 1;

    public BnbRpcClient(HttpClient httpClient, ILogger<BnbRpcClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (_httpClient.BaseAddress is null)
        {
            _configurationError = "RPC provider is not configured. Missing or invalid 'BnbRpc:Url'. Configure it via ASP.NET Core User Secrets (Development) or environment variables.";
        }
    }

    /// <summary>
    /// Queries the current block number on BSC network.
    /// </summary>
    public async Task<long> GetBlockNumberAsync(CancellationToken ct)
    {
        EnsureConfigured();
        ct.ThrowIfCancellationRequested();

        var response = await SendJsonRpcRequestAsync<string>("eth_blockNumber", Array.Empty<object>(), ct);
        
        if (string.IsNullOrWhiteSpace(response))
            throw new InvalidOperationException("eth_blockNumber returned null or empty response.");

        // Convert hex string to long
        var blockNumber = ParseHexToLong(response);
        _logger.LogDebug("Retrieved block number: {BlockNumber}", blockNumber);
        
        return blockNumber;
    }

    /// <summary>
    /// Queries logs matching the specified filter criteria.
    /// Maps eth_getLogs response to LogEvent model.
    /// Automatically clamps block ranges to 3000 blocks maximum and retries on limit exceeded errors.
    /// </summary>
    public async Task<IReadOnlyList<LogEvent>> GetLogsAsync(LogFilter filter, CancellationToken ct)
    {
        EnsureConfigured();
        ct.ThrowIfCancellationRequested();
        if (filter == null) throw new ArgumentNullException(nameof(filter));

        // Clamp block range to maximum 3000 blocks to avoid RPC limit exceeded errors
        var fromBlock = ParseHexToLong(filter.FromBlock);
        var toBlock = filter.ToBlock == "latest" ? 0 : ParseHexToLong(filter.ToBlock);
        
        if (toBlock > 0 && fromBlock > 0)
        {
            var range = toBlock - fromBlock;
            if (range > 3000)
            {
                _logger.LogWarning("Block range {Range} exceeds 3000, clamping to 3000 blocks", range);
                fromBlock = toBlock - 3000;
            }
        }

        // Build eth_getLogs filter parameter
        var ethFilter = new
        {
            fromBlock = fromBlock > 0 ? $"0x{fromBlock:x}" : (filter.FromBlock ?? "latest"),
            toBlock = filter.ToBlock ?? "latest",
            address = filter.Addresses.Count == 0 ? null : (filter.Addresses.Count == 1 ? filter.Addresses[0] : (object)filter.Addresses.ToList()),
            topics = filter.Topics.Count == 0 ? null : (object)filter.Topics.Select(t => t.ToList()).ToList()
        };

        try
        {
            var response = await SendJsonRpcRequestAsync<JsonElement>("eth_getLogs", new[] { ethFilter }, ct);
            return ParseLogsFromResponse(response);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("-32005") || ex.Message.Contains("limit exceeded"))
        {
            // Retry with smaller range if limit exceeded
            _logger.LogWarning("RPC limit exceeded, retrying with half block range");
            return await GetLogsWithSmallerRangeAsync(filter, fromBlock, toBlock, ct);
        }
    }

    /// <summary>
    /// Retries log query with half the block range when limit is exceeded.
    /// </summary>
    private async Task<IReadOnlyList<LogEvent>> GetLogsWithSmallerRangeAsync(LogFilter filter, long fromBlock, long toBlock, CancellationToken ct)
    {
        // If toBlock is 0, it means "latest" was used - get current block
        if (toBlock == 0)
        {
            toBlock = await GetBlockNumberAsync(ct);
        }

        var range = toBlock - fromBlock;
        if (range <= 100)
        {
            // Already too small, can't split further
            _logger.LogError("Cannot split range further (range={Range}), returning empty", range);
            return Array.Empty<LogEvent>();
        }

        // Split range in half
        var halfRange = range / 2;
        var newFromBlock = toBlock - halfRange;

        var reducedFilter = new
        {
            fromBlock = $"0x{newFromBlock:x}",
            toBlock = $"0x{toBlock:x}",
            address = filter.Addresses.Count == 0 ? null : (filter.Addresses.Count == 1 ? filter.Addresses[0] : (object)filter.Addresses.ToList()),
            topics = filter.Topics.Count == 0 ? null : (object)filter.Topics.Select(t => t.ToList()).ToList()
        };

        try
        {
            var response = await SendJsonRpcRequestAsync<JsonElement>("eth_getLogs", new[] { reducedFilter }, ct);
            _logger.LogInformation("Successfully retrieved logs with reduced range: {NewFromBlock}-{ToBlock}", newFromBlock, toBlock);
            return ParseLogsFromResponse(response);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("-32005") || ex.Message.Contains("limit exceeded"))
        {
            // Still too large, return empty rather than infinite recursion
            _logger.LogError("Still exceeds limit after halving range, returning empty");
            return Array.Empty<LogEvent>();
        }
    }

    /// <summary>
    /// Parses log events from JSON-RPC response.
    /// </summary>
    private IReadOnlyList<LogEvent> ParseLogsFromResponse(JsonElement response)
    {
        if (response.ValueKind == JsonValueKind.Null || response.ValueKind == JsonValueKind.Undefined)
            return Array.Empty<LogEvent>();

        var logs = new List<LogEvent>();
        
        if (response.ValueKind == JsonValueKind.Array)
        {
            foreach (var logElement in response.EnumerateArray())
            {
                var log = MapJsonElementToLogEvent(logElement);
                if (log != null)
                    logs.Add(log);
            }
        }

        _logger.LogDebug("Retrieved {LogCount} logs matching filter", logs.Count);
        return logs.AsReadOnly();
    }

    /// <summary>
    /// Queries the transaction receipt for a given transaction hash.
    /// </summary>
    public async Task<TransactionReceipt?> GetReceiptAsync(string txHash, CancellationToken ct)
    {
        EnsureConfigured();
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(txHash))
            throw new ArgumentException("Transaction hash cannot be null or empty.", nameof(txHash));

        var response = await SendJsonRpcRequestAsync<JsonElement>("eth_getTransactionReceipt", new[] { txHash }, ct);
        
        if (response.ValueKind == JsonValueKind.Null || response.ValueKind == JsonValueKind.Undefined)
        {
            _logger.LogDebug("Transaction receipt not found for hash: {TxHash}", txHash);
            return null;
        }

        var receipt = MapJsonElementToTransactionReceipt(response);
        _logger.LogDebug("Retrieved transaction receipt for hash: {TxHash}", txHash);
        
        return receipt;
    }

    /// <summary>
    /// Sends a JSON-RPC 2.0 request and deserializes the result field.
    /// Throws HttpRequestException on network or RPC errors.
    /// </summary>
    private async Task<T> SendJsonRpcRequestAsync<T>(string method, object[] @params, CancellationToken ct)
    {
        EnsureConfigured();
        var id = Interlocked.Increment(ref _requestId);
        
        var request = new JsonRpcRequest
        {
            JsonRpc = "2.0",
            Method = method,
            Params = @params,
            Id = id
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(request, JsonOptions),
                Encoding.UTF8,
                "application/json")
        };

        _logger.LogDebug("RPC Request: method={Method}, id={Id}", method, id);

        using var httpResponse = await _httpClient.SendAsync(httpRequest, ct).ConfigureAwait(false);

        if (!httpResponse.IsSuccessStatusCode)
        {
            var errorBody = await httpResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var snippet = errorBody.Length > 300 ? errorBody[..300] : errorBody;
            throw new HttpRequestException(
                $"BNB RPC HTTP request failed with {(int)httpResponse.StatusCode} {httpResponse.ReasonPhrase}. Response: {snippet}");
        }

        var responseBody = await httpResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        JsonRpcResponse<T>? rpcResponse;
        try
        {
            rpcResponse = JsonSerializer.Deserialize<JsonRpcResponse<T>>(responseBody, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deserialize RPC response body: {Body}", 
                responseBody.Length > 500 ? responseBody[..500] : responseBody);
            throw new InvalidOperationException("Failed to parse RPC response.", ex);
        }

        if (rpcResponse == null)
            throw new InvalidOperationException("RPC response was null.");

        if (rpcResponse.Error != null)
        {
            throw new InvalidOperationException(
                $"RPC error (code {rpcResponse.Error.Code}): {rpcResponse.Error.Message}");
        }

        if (rpcResponse.Result == null)
            throw new InvalidOperationException("RPC result was null.");

        return rpcResponse.Result;
    }

    private void EnsureConfigured()
    {
        if (!string.IsNullOrWhiteSpace(_configurationError))
            throw new InvalidOperationException(_configurationError);
    }

    /// <summary>
    /// Maps JSON-RPC log response to LogEvent model.
    /// </summary>
    private static LogEvent? MapJsonElementToLogEvent(JsonElement logElement)
    {
        try
        {
            var log = new LogEvent
            {
                Address = GetStringProperty(logElement, "address") ?? string.Empty,
                Topics = GetArrayProperty(logElement, "topics") ?? Array.Empty<string>(),
                Data = GetStringProperty(logElement, "data") ?? string.Empty,
                BlockNumber = ParseHexToLong(GetStringProperty(logElement, "blockNumber")),
                TransactionHash = GetStringProperty(logElement, "transactionHash") ?? string.Empty,
                LogIndex = ParseHexToLong(GetStringProperty(logElement, "logIndex")),
                BlockHash = GetStringProperty(logElement, "blockHash") ?? string.Empty,
                TransactionIndex = ParseHexToLong(GetStringProperty(logElement, "transactionIndex"))
            };

            return log;
        }
        catch (Exception)
        {
            // Log parsing error but continue processing other logs
            return null;
        }
    }

    /// <summary>
    /// Maps JSON-RPC transaction receipt response to TransactionReceipt model.
    /// </summary>
    private static TransactionReceipt MapJsonElementToTransactionReceipt(JsonElement receiptElement)
    {
        var receipt = new TransactionReceipt
        {
            TransactionHash = GetStringProperty(receiptElement, "transactionHash") ?? string.Empty,
            TransactionIndex = ParseHexToLong(GetStringProperty(receiptElement, "transactionIndex")),
            BlockHash = GetStringProperty(receiptElement, "blockHash") ?? string.Empty,
            BlockNumber = ParseHexToLong(GetStringProperty(receiptElement, "blockNumber")),
            ContractAddress = GetStringProperty(receiptElement, "contractAddress"),
            GasUsed = ParseHexToLong(GetStringProperty(receiptElement, "gasUsed")),
            GasLimit = ParseHexToLong(GetStringProperty(receiptElement, "gas")),
            From = GetStringProperty(receiptElement, "from") ?? string.Empty,
            To = GetStringProperty(receiptElement, "to"),
            Status = ParseHexToLong(GetStringProperty(receiptElement, "status"))
        };

        return receipt;
    }

    /// <summary>
    /// Helper: safely extract string property from JSON element.
    /// </summary>
    private static string? GetStringProperty(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var property))
        {
            if (property.ValueKind == JsonValueKind.String)
                return property.GetString();
            if (property.ValueKind == JsonValueKind.Null)
                return null;
        }
        return null;
    }

    /// <summary>
    /// Helper: safely extract string array property from JSON element.
    /// </summary>
    private static IReadOnlyList<string>? GetArrayProperty(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Array)
        {
            var arr = new List<string>();
            foreach (var item in property.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                    arr.Add(item.GetString() ?? string.Empty);
            }
            return arr.AsReadOnly();
        }
        return null;
    }

    /// <summary>
    /// Helper: parses hex string to long. Handles null/empty gracefully.
    /// </summary>
    private static long ParseHexToLong(string? hexValue)
    {
        if (string.IsNullOrWhiteSpace(hexValue))
            return 0L;

        // Remove 0x prefix if present
        var hex = hexValue.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? hexValue[2..]
            : hexValue;

        try
        {
            return long.Parse(hex, System.Globalization.NumberStyles.HexNumber);
        }
        catch
        {
            return 0L;
        }
    }

    /// <summary>
    /// JSON-RPC 2.0 request wrapper.
    /// </summary>
    private sealed class JsonRpcRequest
    {
        [JsonPropertyName("jsonrpc")]
        public string JsonRpc { get; set; } = "2.0";

        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("method")]
        public string Method { get; set; } = string.Empty;

        [JsonPropertyName("params")]
        public object[]? Params { get; set; }
    }

    /// <summary>
    /// JSON-RPC 2.0 response wrapper.
    /// </summary>
    private sealed class JsonRpcResponse<T>
    {
        [JsonPropertyName("jsonrpc")]
        public string? JsonRpc { get; set; }

        [JsonPropertyName("id")]
        public int? Id { get; set; }

        [JsonPropertyName("result")]
        public T? Result { get; set; }

        [JsonPropertyName("error")]
        public JsonRpcError? Error { get; set; }
    }

    /// <summary>
    /// JSON-RPC 2.0 error object.
    /// </summary>
    private sealed class JsonRpcError
    {
        [JsonPropertyName("code")]
        public int Code { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        [JsonPropertyName("data")]
        public JsonElement? Data { get; set; }
    }
}
