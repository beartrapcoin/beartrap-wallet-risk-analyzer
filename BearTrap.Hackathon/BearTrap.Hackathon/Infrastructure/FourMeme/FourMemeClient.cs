using BearTrap.Hackathon.Application.Abstractions;
using BearTrap.Hackathon.Domain;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BearTrap.Hackathon.Infrastructure.FourMeme;

/// <summary>
/// Implements Four.Meme REST API client for querying the main token list.
/// Handles all Four.Meme-specific HTTP requests and response deserialization.
/// Uses typed HttpClient for dependency injection.
/// </summary>
public sealed class FourMemeClient : IFourMemeClient
{
    private readonly HttpClient _httpClient;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public FourMemeClient(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<bool> IsTokenOnMainListAsync(string tokenAddress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(tokenAddress)) 
            return false;

        var wanted = tokenAddress.Trim().ToLowerInvariant();
        var latest = await GetLatestTokensAsync(1, ct);
        return latest.Any(t => string.Equals(t.Address, wanted, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<IReadOnlyList<FourMemeListedToken>> GetMainListAsync(int pageSize, CancellationToken ct)
    {
        var tokens = await QueryTokensAsync(null, pageIndex: 1, pageSize, ct);

        return tokens
            .Select(x => new FourMemeListedToken
            {
                Address = x.Address.Trim().ToLowerInvariant(),
                ImageUrl = x.ImageUrl
            })
            .ToList();
    }

    public async Task<IEnumerable<LatestToken>> GetLatestTokensAsync(int count, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var pageSize = Math.Max(count, 30);

        var list = (await QueryTokensAsync(null, pageIndex: 1, pageSize, ct))
            .Take(count)
            .ToList();

        return list;
    }

    public async Task<IReadOnlyList<LatestToken>> QueryTokensAsync(
        string? tokenName,
        int pageIndex,
        int pageSize,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var normalizedName = (tokenName ?? string.Empty).Trim();
        var isSearchMode = !string.IsNullOrWhiteSpace(normalizedName);
        var orderBy = isSearchMode ? "Query" : "TimeDesc";
        var safePageIndex = Math.Max(1, pageIndex);
        var safePageSize = Math.Max(1, pageSize);

        var url =
            $"https://four.meme/meme-api/v1/private/token/query" +
            $"?orderBy={orderBy}&tokenName={Uri.EscapeDataString(normalizedName)}&listedPancake=false&pageIndex={safePageIndex}&pageSize={safePageSize}&symbol=&labels=";

        using var resp = await _httpClient.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadAsStringAsync(ct);
        var dto = JsonSerializer.Deserialize<FourMemeQueryResponse>(json, JsonOptions);
        var data = dto?.Data ?? new List<FourMemeTokenDto>();

        return data
            .Where(x => !string.IsNullOrWhiteSpace(x.Address))
            .Select(x => new LatestToken(
                Address: x.Address!.Trim().ToLowerInvariant(),
                Name: x.ShortName?.Trim() ?? x.Name?.Trim() ?? string.Empty,
                Symbol: x.Symbol?.Trim() ?? string.Empty,
                Creator: x.UserAddress?.Trim() ?? string.Empty,
                CreatedAt: FromUnixMs(x.LaunchTime),
                ImageUrl: x.Image?.Trim()))
            .ToList();
    }

    public async Task<IReadOnlyList<string>> GetMainListAddressesAsync(
        string orderBy,
        bool listedPancake,
        int pageIndex,
        int pageSize,
        string? labels,
        string? tokenName,
        string? symbol,
        CancellationToken ct)
    {
        var baseUrl = "https://four.meme/meme-api/v1/private/token/query";

        var qs = new List<string>
        {
            $"orderBy={Uri.EscapeDataString(orderBy)}",
            $"listedPancake={(listedPancake ? "true" : "false")}",
            $"pageIndex={pageIndex}",
            $"pageSize={pageSize}",
            $"tokenName={Uri.EscapeDataString(tokenName ?? "")}",
            $"symbol={Uri.EscapeDataString(symbol ?? "")}",
        };

        var url = baseUrl + "?" + string.Join("&", qs);

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("accept", "application/json");
        req.Headers.TryAddWithoutValidation("user-agent", "BearTrap/1.0");

        using var resp = await _httpClient.SendAsync(req, ct).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"FourMeme token/query failed {(int)resp.StatusCode} {resp.ReasonPhrase}. Body: {body}");

        var dto = JsonSerializer.Deserialize<FourMemeQueryResponse>(body, JsonOptions);
        if (dto?.Code != 0 || dto.Data == null) 
            return Array.Empty<string>();

        return dto.Data
            .Select(x => x.Address)
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Select(a => a!.Trim().ToLowerInvariant())
            .Distinct()
            .ToList();
    }

    private static DateTimeOffset FromUnixMs(long? ms)
        => ms is null or <= 0 ? DateTimeOffset.UtcNow : DateTimeOffset.FromUnixTimeMilliseconds(ms.Value);

    // ===== DTO Classes for Four.Meme API Response =====

    private sealed class FourMemeQueryResponse
    {
        [JsonPropertyName("code")]
        public int Code { get; set; }

        [JsonPropertyName("msg")]
        public string? Msg { get; set; }

        [JsonPropertyName("data")]
        public List<FourMemeTokenDto>? Data { get; set; }
    }

    private sealed class FourMemeTokenDto
    {
        [JsonPropertyName("address")]
        public string? Address { get; set; }

        [JsonPropertyName("image")]
        public string? Image { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("shortName")]
        public string? ShortName { get; set; }

        [JsonPropertyName("symbol")]
        public string? Symbol { get; set; }

        [JsonPropertyName("userAddress")]
        public string? UserAddress { get; set; }

        [JsonPropertyName("launchTime")]
        public long? LaunchTime { get; set; }
    }
}
