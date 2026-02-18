using BearTrap.Hackathon.Application.Abstractions;
using BearTrap.Hackathon.Domain;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BearTrap.Hackathon.Infrastructure.Bitquery;

/// <summary>
/// Implements Bitquery GraphQL API client for querying token events.
/// Handles all Bitquery-specific HTTP requests, GraphQL queries, and response deserialization.
/// Uses typed HttpClient for dependency injection.
/// </summary>
public sealed class BitqueryClient : IBitqueryClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<BitqueryClient> _logger;
    private readonly string _endpoint;
    private readonly string _token;
    private readonly string _fourMemeFactoryAddress;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public BitqueryClient(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<BitqueryClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        if (configuration == null) throw new ArgumentNullException(nameof(configuration));

        _endpoint = (configuration["Bitquery:Endpoint"] ?? "").Trim();
        _token = (configuration["Bitquery:Token"] ?? "").Trim();
        _fourMemeFactoryAddress = (configuration["FourMeme:FactoryAddress"] ?? "").Trim();

        if (!string.IsNullOrWhiteSpace(_fourMemeFactoryAddress))
        {
            // Normalize address
            if (!_fourMemeFactoryAddress.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                _fourMemeFactoryAddress = "0x" + _fourMemeFactoryAddress;

            _fourMemeFactoryAddress = _fourMemeFactoryAddress.ToLowerInvariant();
        }
    }

    public async Task<bool> IsTokenFromFourMemeAsync(string tokenAddress, CancellationToken ct)
    {
        EnsureConfigured();
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(tokenAddress))
            return false;

        var wanted = tokenAddress.Trim().ToLowerInvariant();
        int scanLimit = 1;

        var latest = await GetLatestTokensAsync(scanLimit, ct);
        return latest.Any(t =>
            t.Address.Trim().ToLowerInvariant() == wanted);
    }

    public async Task<IReadOnlyList<LatestToken>> GetLatestTokensAsync(int count, CancellationToken ct)
    {
        EnsureConfigured();
        ct.ThrowIfCancellationRequested();
        if (count <= 0) return Array.Empty<LatestToken>();

        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint);
        request.Version = HttpVersion.Version11;
        request.VersionPolicy = HttpVersionPolicy.RequestVersionOrLower;

        request.Headers.Remove("Authorization");
        request.Headers.TryAddWithoutValidation("X-API-KEY", _token);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);

        var gql = @"
query($limit: Int!, $tos: [String!]!) {
  EVM(dataset: realtime, network: bsc) {
    Events(
      where: {
        Transaction: { To: { in: $tos } }
        Log: { Signature: { Name: { is: ""TokenCreate"" } } }
      }
      limit: { count: $limit }
      orderBy: { descending: Block_Time }
    ) {
      Block { Time }
      Transaction { From To }
      Arguments {
        Name
        Value {
          ... on EVM_ABI_Address_Value_Arg { address }
          ... on EVM_ABI_String_Value_Arg { string }
        }
      }
    }
  }
}
";

        var payload = new GraphqlRequest
        {
            Query = gql,
            Variables = new Dictionary<string, object?>
            {
                ["limit"] = count,
                ["tos"] = new[] { _fourMemeFactoryAddress }
            }
        };

        request.Content = new StringContent(
            JsonSerializer.Serialize(payload, JsonOptions),
            Encoding.UTF8,
            "application/json");

        using var resp = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        var respBody = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        _logger.LogInformation("BITQUERY HTTP {Status}. RAW (first 1200): {Body}",
            (int)resp.StatusCode,
            respBody.Length > 1200 ? respBody[..1200] : respBody);

        if (!resp.IsSuccessStatusCode)
        {
            var snippet = string.IsNullOrWhiteSpace(respBody)
                ? ""
                : (respBody.Length > 300 ? respBody[..300] : respBody);

            throw new HttpRequestException(
                $"Bitquery request failed with {(int)resp.StatusCode} {resp.ReasonPhrase}. Response: {snippet}");
        }

        if (string.IsNullOrWhiteSpace(respBody))
            return Array.Empty<LatestToken>();

        GraphqlResponse? gqlResp;
        try
        {
            gqlResp = JsonSerializer.Deserialize<GraphqlResponse>(respBody, JsonOptions);
        }
        catch (Exception ex)
        {
            var snippet = respBody.Length > 300 ? respBody[..300] : respBody;
            throw new InvalidOperationException($"Failed to deserialize Bitquery response. Snippet: {snippet}", ex);
        }

        if (gqlResp?.Errors is { Count: > 0 })
        {
            var msg = string.Join(" | ",
                gqlResp.Errors
                    .Select(e => e.Message)
                    .Where(m => !string.IsNullOrWhiteSpace(m)));

            throw new InvalidOperationException("Bitquery GraphQL errors: " + msg);
        }

        var events = gqlResp?.Data?.EVM?.Events;
        if (events == null || events.Count == 0)
            return Array.Empty<LatestToken>();

        var results = new List<LatestToken>(events.Count);

        foreach (var ev in events)
        {
            if (ct.IsCancellationRequested) break;

            // token address (key)
            var address = ev.Arguments?
                .FirstOrDefault(a => NameIs(a.Name, "token", "address", "tokenAddress"))?
                .Value?.Address;

            if (string.IsNullOrWhiteSpace(address))
                continue;

            var name = ev.Arguments?
                .FirstOrDefault(a => NameIs(a.Name, "name", "tokenName"))?
                .Value?.String ?? string.Empty;

            var symbol = ev.Arguments?
                .FirstOrDefault(a => NameIs(a.Name, "symbol", "ticker", "tokenSymbol"))?
                .Value?.String ?? string.Empty;

            // creator: argument.creator.address or Transaction.From
            var creator = ev.Arguments?
                .FirstOrDefault(a => NameIs(a.Name, "creator", "sender"))?
                .Value?.Address
                ?? ev.Transaction?.From
                ?? string.Empty;

            // createdAt
            var createdAt = DateTimeOffset.UtcNow;
            if (!string.IsNullOrWhiteSpace(ev.Block?.Time) &&
                DateTimeOffset.TryParse(ev.Block.Time, out var parsed))
            {
                createdAt = parsed;
            }

            if (string.IsNullOrWhiteSpace(address))
                continue;

            address = address.Trim().ToLowerInvariant();

            // Validate: Transaction.To must match factory address (safety check)
            var to = ev.Transaction?.To?.Trim();
            if (!string.IsNullOrWhiteSpace(to) &&
                !string.Equals(to, _fourMemeFactoryAddress, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            results.Add(new LatestToken(
                address.Trim(),
                name.Trim(),
                symbol.Trim(),
                creator.Trim(),
                createdAt));
        }

        return results.Take(count).ToList();
    }

    private static bool NameIs(string? actual, params string[] names)
    {
        if (string.IsNullOrWhiteSpace(actual)) return false;
        var a = actual.Trim();
        return names.Any(n => string.Equals(a, n, StringComparison.OrdinalIgnoreCase));
    }

    private void EnsureConfigured()
    {
        var missingKeys = new List<string>();

        if (string.IsNullOrWhiteSpace(_endpoint))
            missingKeys.Add("Bitquery:Endpoint");

        if (string.IsNullOrWhiteSpace(_token))
            missingKeys.Add("Bitquery:Token");

        if (string.IsNullOrWhiteSpace(_fourMemeFactoryAddress))
            missingKeys.Add("FourMeme:FactoryAddress");

        if (missingKeys.Count > 0)
        {
            throw new InvalidOperationException(
                $"Bitquery provider is not configured. Missing configuration keys: {string.Join(", ", missingKeys)}. Configure them via ASP.NET Core User Secrets (Development) or environment variables.");
        }

        if (!Uri.TryCreate(_endpoint, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new InvalidOperationException(
                "Bitquery provider is not configured. 'Bitquery:Endpoint' must be an absolute URL.");
        }
    }

    // ===== DTO Classes for GraphQL Request/Response =====

    private sealed class GraphqlRequest
    {
        [JsonPropertyName("query")]
        public string? Query { get; set; }

        [JsonPropertyName("variables")]
        public Dictionary<string, object?>? Variables { get; set; }
    }

    private sealed class GraphqlResponse
    {
        [JsonPropertyName("data")]
        public GraphqlData? Data { get; set; }

        [JsonPropertyName("errors")]
        public List<GraphqlError>? Errors { get; set; }
    }

    private sealed class GraphqlError
    {
        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }

    private sealed class GraphqlData
    {
        [JsonPropertyName("EVM")]
        public EvmSection? EVM { get; set; }
    }

    private sealed class EvmSection
    {
        [JsonPropertyName("Events")]
        public List<EventRow>? Events { get; set; }
    }

    private sealed class EventRow
    {
        [JsonPropertyName("Block")]
        public BlockInfo? Block { get; set; }

        [JsonPropertyName("Transaction")]
        public TransactionInfo? Transaction { get; set; }

        [JsonPropertyName("Arguments")]
        public List<EventArgument>? Arguments { get; set; }
    }

    private sealed class BlockInfo
    {
        [JsonPropertyName("Time")]
        public string? Time { get; set; }
    }

    private sealed class TransactionInfo
    {
        [JsonPropertyName("From")]
        public string? From { get; set; }

        [JsonPropertyName("To")]
        public string? To { get; set; }
    }

    private sealed class EventArgument
    {
        [JsonPropertyName("Name")]
        public string? Name { get; set; }

        [JsonPropertyName("Value")]
        public ArgValue? Value { get; set; }
    }

    private sealed class ArgValue
    {
        [JsonPropertyName("address")]
        public string? Address { get; set; }

        [JsonPropertyName("string")]
        public string? String { get; set; }
    }
}
