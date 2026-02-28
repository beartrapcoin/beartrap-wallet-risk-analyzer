using BearTrap.Hackathon.Application.Abstractions;
using BearTrap.Hackathon.Domain;
using Microsoft.Extensions.Logging;
using System.Globalization;
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
    private readonly ILogger<FourMemeClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public FourMemeClient(HttpClient httpClient, ILogger<FourMemeClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
        var tokens = await QueryTokensAsync(null, FourMemeOrderBy.TimeDesc, pageIndex: 1, pageSize, ct);

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

        var list = (await QueryTokensAsync(null, FourMemeOrderBy.TimeDesc, pageIndex: 1, pageSize, ct))
            .Take(count)
            .ToList();

        return list;
    }

    public async Task<IReadOnlyList<LatestToken>> QueryTokensAsync(
        string? tokenName,
        FourMemeOrderBy orderBy,
        int pageIndex,
        int pageSize,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var normalizedName = (tokenName ?? string.Empty).Trim();
        var isSearchMode = !string.IsNullOrWhiteSpace(normalizedName);
        var apiOrderBy = ResolveApiOrderBy(orderBy, isSearchMode);
        var safePageIndex = Math.Max(1, pageIndex);
        var safePageSize = Math.Max(1, pageSize);

        var url =
            $"https://four.meme/meme-api/v1/private/token/query" +
            $"?orderBy={apiOrderBy}&tokenName={Uri.EscapeDataString(normalizedName)}&listedPancake=false&pageIndex={safePageIndex}&pageSize={safePageSize}&symbol=&labels=";

        using var resp = await _httpClient.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadAsStringAsync(ct);
        LogQueryJsonPreview(json);

        var dto = JsonSerializer.Deserialize<FourMemeQueryResponse>(json, JsonOptions);
        var data = dto?.Data ?? new List<FourMemeTokenDto>();

        return data
            .Where(x => !string.IsNullOrWhiteSpace(x.Address))
            .Select(x =>
            {
                var createdAt = ParseUnixSecondsString(x.CreateDate) ?? FromUnixMs(x.LaunchTime);
                var modifiedAt = ParseUnixSecondsString(x.ModifyDate);

                return new LatestToken(
                    Address: x.Address!.Trim().ToLowerInvariant(),
                    Name: x.ShortName?.Trim() ?? x.Name?.Trim() ?? string.Empty,
                    Symbol: x.Symbol?.Trim() ?? string.Empty,
                    Creator: x.UserAddress?.Trim() ?? string.Empty,
                    CreatedAt: createdAt,
                    ImageUrl: x.Image?.Trim(),
                    ProgressPercent: ExtractProgressPercent(x),
                    CreatorUserId: ExtractCreatorUserId(x),
                    WebUrl: x.WebUrl?.Trim(),
                    TelegramUrl: x.TelegramUrl?.Trim(),
                    TwitterUrl: x.TwitterUrl?.Trim(),
                    ModifiedAt: modifiedAt,
                    Description: x.Description?.Trim(),
                    Desc: x.Desc?.Trim(),
                    Extra: ExtractStringExtra(x.Extra));
            })
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

    private static DateTimeOffset? ParseUnixSecondsString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ts))
        {
            const long minUnixSeconds = -62135596800;
            const long maxUnixSeconds = 253402300799;
            const long minUnixMilliseconds = -62135596800000;
            const long maxUnixMilliseconds = 253402300799999;

            if (ts >= minUnixSeconds && ts <= maxUnixSeconds)
            {
                return DateTimeOffset.FromUnixTimeSeconds(ts);
            }

            if (ts >= minUnixMilliseconds && ts <= maxUnixMilliseconds)
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(ts);
            }
        }

        return null;
    }

    private void LogQueryJsonPreview(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("data", out var dataElement) &&
                dataElement.ValueKind == JsonValueKind.Array &&
                dataElement.GetArrayLength() > 0)
            {
                var firstItemRaw = dataElement[0].GetRawText();
                var preview = firstItemRaw.Length > 5000 ? firstItemRaw[..5000] : firstItemRaw;
                _logger.LogInformation("FourMeme token/query first data item JSON preview (len={Length}): {JsonPreview}", firstItemRaw.Length, preview);
                return;
            }
        }
        catch (JsonException)
        {
        }

        var rawPreview = json.Length > 5000 ? json[..5000] : json;
        _logger.LogInformation("FourMeme token/query raw JSON preview (len={Length}): {JsonPreview}", json.Length, rawPreview);
    }

    private static decimal? ExtractProgressPercent(FourMemeTokenDto token)
    {
        var raw = token.BondingProgress ?? token.CurveProgress ?? token.Progress ?? token.ProgressRate;
        raw ??= TryGetProgressFromExtra(token.Extra);

        if (!raw.HasValue)
        {
            return null;
        }

        var value = raw.Value;

        if (value is >= 0m and <= 1m)
        {
            value *= 100m;
        }

        return Math.Clamp(value, 0m, 100m);
    }

    private static string? ExtractCreatorUserId(FourMemeTokenDto token)
    {
        if (token.Extra is null || !TryGetJsonElement(token.Extra, "userId", out var userIdElement))
        {
            return null;
        }

        if (userIdElement.ValueKind == JsonValueKind.Number && userIdElement.TryGetInt64(out var userId))
        {
            return userId.ToString(CultureInfo.InvariantCulture);
        }

        return null;
    }

    private static decimal? TryGetProgressFromExtra(Dictionary<string, JsonElement>? extra)
    {
        if (extra is null || extra.Count == 0)
        {
            return null;
        }

        string[] keys = ["bondingProgress", "curveProgress", "progress", "progressRate"];

        foreach (var key in keys)
        {
            if (TryGetExtraDecimal(extra, key, out var parsed))
            {
                return parsed;
            }
        }

        if (TryGetNestedTokenPriceProgress(extra, out var tokenPriceProgress))
        {
            return tokenPriceProgress;
        }

        if (TryGetProgressFromAmounts(extra, out var amountProgress))
        {
            return amountProgress;
        }

        return null;
    }

    private static bool TryGetProgressFromAmounts(Dictionary<string, JsonElement> extra, out decimal progress)
    {
        progress = default;

        if (!TryGetExtraDecimal(extra, "saleAmount", out var saleAmount) ||
            !TryGetExtraDecimal(extra, "totalAmount", out var totalAmount) ||
            totalAmount <= 0m)
        {
            return false;
        }

        progress = Math.Clamp((saleAmount / totalAmount) * 100m, 0m, 100m);
        return true;
    }

    private static bool TryGetNestedTokenPriceProgress(Dictionary<string, JsonElement> extra, out decimal value)
    {
        value = default;

        if (!TryGetJsonElement(extra, "tokenPrice", out var tokenPriceElement) ||
            tokenPriceElement.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        string[] keys = ["bondingProgress", "curveProgress", "progress", "progressRate"];
        foreach (var key in keys)
        {
            if (TryGetPropertyCaseInsensitive(tokenPriceElement, key, out var nested) &&
                TryParseJsonDecimal(nested, out value))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetJsonElement(Dictionary<string, JsonElement> values, string key, out JsonElement value)
    {
        if (values.TryGetValue(key, out value))
        {
            return true;
        }

        foreach (var pair in values)
        {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                value = pair.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static bool TryGetPropertyCaseInsensitive(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            value = default;
            return false;
        }

        if (element.TryGetProperty(propertyName, out value))
        {
            return true;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static bool TryGetExtraDecimal(Dictionary<string, JsonElement> extra, string key, out decimal value)
    {
        value = default;

        if (extra.TryGetValue(key, out var direct))
        {
            return TryParseJsonDecimal(direct, out value);
        }

        foreach (var pair in extra)
        {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return TryParseJsonDecimal(pair.Value, out value);
            }
        }

        return false;
    }

    private static bool TryParseJsonDecimal(JsonElement element, out decimal value)
    {
        value = default;

        return element.ValueKind switch
        {
            JsonValueKind.Number => element.TryGetDecimal(out value),
            JsonValueKind.String => decimal.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value),
            _ => false
        };
    }

    private static string ResolveApiOrderBy(FourMemeOrderBy orderBy, bool isSearchMode)
    {
        if (isSearchMode)
        {
            return "Query";
        }

        return orderBy switch
        {
            FourMemeOrderBy.Hot => "Hot",
            FourMemeOrderBy.TimeDesc => "TimeDesc",
            FourMemeOrderBy.TradingVolume => "OrderDesc",
            FourMemeOrderBy.Progress => "ProgressDesc",
            FourMemeOrderBy.LastTrade => "LastTradeDesc",
            _ => "Hot"
        };
    }

    private static Dictionary<string, string>? ExtractStringExtra(Dictionary<string, JsonElement>? extra)
    {
        if (extra is null || extra.Count == 0)
        {
            return null;
        }

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in extra)
        {
            if (value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    result[key] = text.Trim();
                }
            }
        }

        return result.Count == 0 ? null : result;
    }

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

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("desc")]
        public string? Desc { get; set; }

        [JsonPropertyName("userAddress")]
        public string? UserAddress { get; set; }

        [JsonPropertyName("launchTime")]
        public long? LaunchTime { get; set; }

        [JsonPropertyName("webUrl")]
        public string? WebUrl { get; set; }

        [JsonPropertyName("telegramUrl")]
        public string? TelegramUrl { get; set; }

        [JsonPropertyName("twitterUrl")]
        public string? TwitterUrl { get; set; }

        [JsonPropertyName("createDate")]
        public string? CreateDate { get; set; }

        [JsonPropertyName("modifyDate")]
        public string? ModifyDate { get; set; }

        [JsonPropertyName("bondingProgress")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public decimal? BondingProgress { get; set; }

        [JsonPropertyName("curveProgress")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public decimal? CurveProgress { get; set; }

        [JsonPropertyName("progress")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public decimal? Progress { get; set; }

        [JsonPropertyName("progressRate")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public decimal? ProgressRate { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? Extra { get; set; }
    }
}
