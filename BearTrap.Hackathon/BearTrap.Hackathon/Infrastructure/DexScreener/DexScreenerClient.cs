using BearTrap.Hackathon.Application.Abstractions;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace BearTrap.Hackathon.Infrastructure.DexScreener;

/// <summary>
/// DexScreener API client for paid visibility checks.
/// </summary>
public sealed class DexScreenerClient : IDexScreenerClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<DexScreenerClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public DexScreenerClient(HttpClient httpClient, ILogger<DexScreenerClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<bool> HasPaidVisibilityAsync(string chainId, string tokenAddress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var normalizedChainId = (chainId ?? string.Empty).Trim();
        var normalizedAddress = (tokenAddress ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(normalizedChainId) || string.IsNullOrWhiteSpace(normalizedAddress))
        {
            return false;
        }

        var url = $"orders/v1/{Uri.EscapeDataString(normalizedChainId)}/{Uri.EscapeDataString(normalizedAddress)}";

        try
        {
            using var response = await _httpClient.GetAsync(url, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug(
                    "DexScreener paid-visibility query failed for {ChainId}/{TokenAddress} with HTTP {StatusCode}",
                    normalizedChainId,
                    normalizedAddress,
                    (int)response.StatusCode);
                return false;
            }

            var content = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(content))
            {
                return false;
            }

            var payload = JsonSerializer.Deserialize<DexScreenerOrdersResponse>(content, JsonOptions);
            if (payload is null)
            {
                return false;
            }

            var hasBoost = payload.Boosts is { Count: > 0 };
            if (hasBoost)
            {
                return true;
            }

            var hasApprovedOrder = payload.Orders?.Any(order =>
                string.Equals(order.Status, "approved", StringComparison.OrdinalIgnoreCase)) == true;

            return hasApprovedOrder;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(
                "DexScreener paid-visibility query timed out for {ChainId}/{TokenAddress}",
                normalizedChainId,
                normalizedAddress);
            return false;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(
                ex,
                "DexScreener paid-visibility query failed for {ChainId}/{TokenAddress}",
                normalizedChainId,
                normalizedAddress);
            return false;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(
                ex,
                "DexScreener paid-visibility response could not be parsed for {ChainId}/{TokenAddress}",
                normalizedChainId,
                normalizedAddress);
            return false;
        }
    }

    private sealed class DexScreenerOrdersResponse
    {
        public List<DexScreenerOrder>? Orders { get; set; }

        public List<DexScreenerBoost>? Boosts { get; set; }
    }

    private sealed class DexScreenerOrder
    {
        public string? Status { get; set; }
    }

    private sealed class DexScreenerBoost
    {
    }
}
