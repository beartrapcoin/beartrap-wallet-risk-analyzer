using BearTrap.Hackathon.Application.Abstractions;
using BearTrap.Hackathon.Data;
using BearTrap.Hackathon.Data.Entities;
using BearTrap.Hackathon.Domain;
using BearTrap.Hackathon.Services.DataSources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace BearTrap.Hackathon.Application.Services;

/// <summary>
/// Application-level service for analyzing token risk.
/// Supports batch analysis to detect patterns within a group of tokens.
/// </summary>
public sealed class RiskAnalyzer
{
    private readonly AppDbContext _db;
    private readonly IFourMemeSource _fourMemeSource;
    private readonly IBnbRpcClient _bnbRpcClient;
    private readonly IDexScreenerClient _dexScreenerClient;
    private readonly ILogger<RiskAnalyzer> _logger;

    // Generic scam keywords for NAME_TOO_GENERIC flag
    private static readonly string[] GenericScamKeywords = new[]
    {
        "token", "coin", "bnb", "moon", "inu", "ai", "100x", "pump", "profit", "doge", "safe", "elon"
    };

    private const string SuspiciousScamActionPattern = @"\b(airdrop|claim|free|giveaway|bonus|presale)\b";

    private static readonly RegexOptions SuspiciousRegexOptions = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private static readonly char[] SuspiciousSpecialChars = ['_', '-', '#', '@', '$', '%', '*'];

    private const string NoSocialsFlagCode = "NO_SOCIALS";
    private const string MetadataChangedFlagCode = "METADATA_CHANGED";
    private const string EmptyDescriptionFlagCode = "EMPTY_DESCRIPTION";
    private const string LinkInDescriptionFlagCode = "LINK_IN_DESCRIPTION";
    private const string BoostedVisibilityFlagCode = "BOOSTED_VISIBILITY";

    private static bool _heuristicExamplesLogged;

    public RiskAnalyzer(
        AppDbContext db,
        IFourMemeSource fourMemeSource,
        IBnbRpcClient bnbRpcClient,
        IDexScreenerClient dexScreenerClient,
        ILogger<RiskAnalyzer> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _fourMemeSource = fourMemeSource ?? throw new ArgumentNullException(nameof(fourMemeSource));
        _bnbRpcClient = bnbRpcClient ?? throw new ArgumentNullException(nameof(bnbRpcClient));
        _dexScreenerClient = dexScreenerClient ?? throw new ArgumentNullException(nameof(dexScreenerClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        LogSuspiciousHeuristicExamples();
    }

    /// <summary>
    /// Analyze a single token for risk. This method wraps AnalyzeBatchAsync for backward compatibility.
    /// </summary>
    public async Task<RiskReport> AnalyzeAsync(LatestToken token, CancellationToken ct)
    {
        var results = await AnalyzeBatchAsync(new[] { token }, ct);
        return results.First().Report;
    }

    /// <summary>
    /// Analyze a batch of tokens for risk, detecting patterns within the batch.
    /// Returns a list of (Token, RiskReport) tuples.
    /// </summary>
    public async Task<List<(LatestToken Token, RiskReport Report)>> AnalyzeBatchAsync(
        IEnumerable<LatestToken> tokens,
        CancellationToken ct)
    {
        var tokenList = tokens.ToList();
        var results = new List<(LatestToken Token, RiskReport Report)>();

        if (tokenList.Count == 0)
            return results;

        // --- Step 1: Compute batch-level aggregates ---
        var creatorCounts = tokenList
            .Where(t => !string.IsNullOrWhiteSpace(t.Creator))
            .GroupBy(t => t.Creator.Trim().ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.Count());

        var nameCounts = tokenList
            .Where(t => !string.IsNullOrWhiteSpace(t.Name))
            .GroupBy(t => NormalizeTokenName(t.Name))
            .ToDictionary(g => g.Key, g => g.Count());

        var imageUrlCounts = tokenList
            .Where(t => !string.IsNullOrWhiteSpace(t.ImageUrl))
            .GroupBy(t => t.ImageUrl!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var firstContractForName = tokenList
            .Select((token, index) => new
            {
                Token = token,
                Index = index,
                NameKey = NormalizeTokenName(token.Name)
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.NameKey))
            .GroupBy(x => x.NameKey)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var withCreatedAt = g
                        .Where(x => x.Token.CreatedAt != default)
                        .OrderBy(x => x.Token.CreatedAt)
                        .ThenBy(x => x.Index)
                        .FirstOrDefault();

                    var first = withCreatedAt ?? g.OrderBy(x => x.Index).First();
                    return NormalizeAddress(first.Token.Address);
                });

        // --- Step 2: Ensure all token snapshots exist ---
        foreach (var token in tokenList)
        {
            if (!await _db.TokenSnapshots.AnyAsync(x => x.Address == token.Address, ct))
            {
                var snapshot = new TokenSnapshotEntity
                {
                    Address = token.Address,
                    Name = token.Name,
                    Symbol = token.Symbol,
                    Creator = token.Creator,
                    CreatedAt = token.CreatedAt == default ? DateTimeOffset.UtcNow : token.CreatedAt
                };

                _db.TokenSnapshots.Add(snapshot);
                try
                {
                    await _db.SaveChangesAsync(ct);
                }
                catch (DbUpdateException)
                {
                    // Possible concurrent insert - verify if the snapshot now exists.
                    var exists = await _db.TokenSnapshots.AnyAsync(x => x.Address == token.Address, ct);
                    if (!exists)
                    {
                        throw;
                    }
                }
            }
        }

        // --- Step 3: Analyze each token ---
        foreach (var token in tokenList)
        {
            var flags = new List<RiskFlag>();

            // Batch-based flags
            AnalyzeBatchFlags(token, creatorCounts, nameCounts, firstContractForName, imageUrlCounts, flags);

            // Database-based flags
            await AnalyzeDatabaseFlagsAsync(token, flags, ct);

            // Pattern-based flags
            AnalyzePatternFlags(token, flags);

            // External paid promotion check (DexScreener)
            await AnalyzeDexScreenerFlagsAsync(token, flags, ct);

            var score = Math.Min(100, flags.Sum(f => f.Points));
            results.Add((token, new RiskReport(score, flags)));
        }

        return results;
    }

    /// <summary>
    /// Analyze flags based on patterns within the current batch.
    /// </summary>
    private void AnalyzeBatchFlags(
        LatestToken token,
        Dictionary<string, int> creatorCounts,
        Dictionary<string, int> nameCounts,
        Dictionary<string, string> firstContractForName,
        Dictionary<string, int> imageUrlCounts,
        List<RiskFlag> flags)
    {
        // CREATOR_BURST (+30): Same creator appears >= 3 times in batch
        var creatorKey = token.Creator?.Trim().ToLowerInvariant() ?? "";
        if (!string.IsNullOrWhiteSpace(creatorKey) && creatorCounts.TryGetValue(creatorKey, out var creatorCount))
        {
            if (creatorCount >= 3)
            {
                AddFlagIfMissing(flags, new RiskFlag(
                    "CREATOR_BURST",
                    "Creator burst deploys",
                    $"Creator deployed {creatorCount} tokens in this batch.",
                    30));
            }
        }

        // DUPLICATE_NAME_IN_BATCH (+20): Name appears > 1 time in batch
        var nameKey = NormalizeTokenName(token.Name);
        if (!string.IsNullOrWhiteSpace(nameKey) && nameCounts.TryGetValue(nameKey, out var nameCount))
        {
            var tokenAddress = NormalizeAddress(token.Address);
            var hasFirst = firstContractForName.TryGetValue(nameKey, out var firstContractAddress);
            var duplicateName = hasFirst && !string.Equals(tokenAddress, firstContractAddress, StringComparison.Ordinal);

            // Hackathon: only tokens after the first occurrence are marked as duplicates
            if (nameCount > 1 && duplicateName)
            {
                AddFlagIfMissing(flags, new RiskFlag(
                    "DUPLICATE_NAME_IN_BATCH",
                    "Duplicate name in batch",
                    $"Name '{token.Name}' appears {nameCount} times in this batch.",
                    20));
            }
        }

        // IMAGE_REUSED (+10): Same image URL appears > 1 time in current list
        var imageUrl = token.ImageUrl?.Trim();
        if (!string.IsNullOrWhiteSpace(imageUrl) && imageUrlCounts.TryGetValue(imageUrl, out var imageCount) && imageCount > 1)
        {
            AddFlagIfMissing(flags, new RiskFlag(
                "IMAGE_REUSED",
                "Image reused",
                "Another token in the current list uses the same image URL.",
                10));
        }
    }

    /// <summary>
    /// Analyze flags based on database history.
    /// </summary>
    private async Task AnalyzeDatabaseFlagsAsync(
        LatestToken token,
        List<RiskFlag> flags,
        CancellationToken ct)
    {
        // CREATOR_SPAM (+35): Creator has created >= 3 tokens in last 24 hours (database check)
        var since = DateTimeOffset.UtcNow.AddHours(-24);
        var creatorTokens = await _db.TokenSnapshots
            .Where(x => x.Creator == token.Creator)
            .AsNoTracking()
            .ToListAsync(ct);

        var creatorCount = creatorTokens.Count(x => x.CreatedAt >= since);
        if (creatorCount >= 3)
        {
            AddFlagIfMissing(flags, new RiskFlag(
                "CREATOR_SPAM",
                "Creator spam",
                $"Creator {token.Creator} created {creatorCount} tokens in the last 24 hours.",
                35));
        }

        // Hackathon: NAME_REUSED only when the same creator reused the name or symbol
        var reused = await _db.TokenSnapshots
            .AnyAsync(x =>
                x.Address != token.Address &&
                x.Creator == token.Creator &&
                (
                    x.Name == token.Name ||
                    x.Symbol == token.Symbol
                ), ct);

        if (reused)
        {
            AddFlagIfMissing(flags, new RiskFlag(
                "NAME_REUSED",
                "Name reused by creator",
                "The same creator has previously used this name or symbol.",
                15));
        }
    }

    /// <summary>
    /// Analyze flags based on token name/symbol patterns.
    /// </summary>
    private void AnalyzePatternFlags(LatestToken token, List<RiskFlag> flags)
    {
        var name = token.Name?.Trim() ?? string.Empty;
        var symbol = token.Symbol?.Trim() ?? string.Empty;
        var effectiveDescription = GetEffectiveDescription(token);
        var description = effectiveDescription ?? string.Empty;

        // EMPTY_DESCRIPTION (+5): missing or very short token description
        if ((string.IsNullOrWhiteSpace(effectiveDescription) || description.Length < 10) &&
            !flags.Any(flag => flag.Code == EmptyDescriptionFlagCode))
        {
            AddFlagIfMissing(flags, CreateFlag(EmptyDescriptionFlagCode, "Token description is empty or extremely short."));
        }

        // LINK_IN_DESCRIPTION (+10): description contains external links/domains
        if (!string.IsNullOrWhiteSpace(description) &&
            ContainsDescriptionExternalLink(description) &&
            !flags.Any(flag => flag.Code == LinkInDescriptionFlagCode))
        {
            AddFlagIfMissing(flags, CreateFlag(LinkInDescriptionFlagCode, "Token description contains an external link (possible phishing)."));
        }

        // SYMBOL_TOO_SHORT (+10): Symbol length <= 2
        if (symbol.Length > 0 && symbol.Length <= 2)
        {
            AddFlagIfMissing(flags, new RiskFlag(
                "SYMBOL_TOO_SHORT",
                "Very short symbol",
                $"Symbol '{symbol}' is extremely short ({symbol.Length} characters).",
                10));
        }

        // SYMBOL_RANDOMIZED (+15): Symbol contains >= 2 digits or matches spam pattern
        if (symbol.Length > 0)
        {
            var digitCount = symbol.Count(char.IsDigit);
            var matchesSpamPattern = Regex.IsMatch(symbol, @"^[A-Z]{2,6}\d{2,4}$", RegexOptions.IgnoreCase);

            if (digitCount >= 2 || matchesSpamPattern)
            {
                AddFlagIfMissing(flags, new RiskFlag(
                    "SYMBOL_RANDOMIZED",
                    "Randomized symbol pattern",
                    $"Symbol '{symbol}' appears to be randomly generated or spam-like.",
                    15));
            }
        }

        // NAME_TOO_GENERIC (+15): Name contains >= 2 generic scam keywords
        if (name.Length > 0)
        {
            var nameUpper = name.ToUpperInvariant();
            var matchedKeywords = GenericScamKeywords
                .Where(kw => nameUpper.Contains(kw.ToUpperInvariant()))
                .ToList();

            if (matchedKeywords.Count >= 2)
            {
                AddFlagIfMissing(flags, new RiskFlag(
                    "NAME_TOO_GENERIC",
                    "Generic hype name",
                    $"Name contains generic scam keywords: {string.Join(", ", matchedKeywords)}.",
                    15));
            }
        }

        // NAME_SUS (+10): hard-trigger-only suspicious name detection with debug reason
        var suspiciousReason = DetectSuspiciousNameReason(name, symbol);

        if (!string.IsNullOrWhiteSpace(suspiciousReason) && !flags.Any(flag => flag.Code == "NAME_SUS"))
        {
            AddFlagIfMissing(flags, CreateFlag("NAME_SUS", suspiciousReason));
        }

        var hasNoSocials = string.IsNullOrWhiteSpace(token.WebUrl)
            && string.IsNullOrWhiteSpace(token.TelegramUrl)
            && string.IsNullOrWhiteSpace(token.TwitterUrl);

        if (hasNoSocials && !flags.Any(flag => flag.Code == NoSocialsFlagCode))
        {
            AddFlagIfMissing(flags, CreateFlag(NoSocialsFlagCode, "No website, Telegram or Twitter provided."));
        }

        if (token.ModifiedAt.HasValue)
        {
            var minutesDiff = (token.ModifiedAt.Value - token.CreatedAt).TotalMinutes;
            if (minutesDiff >= 60 && !flags.Any(flag => flag.Code == MetadataChangedFlagCode))
            {
                AddFlagIfMissing(flags, CreateFlag(MetadataChangedFlagCode, $"Metadata modified {(int)Math.Round(minutesDiff)} minutes after creation."));
            }
        }
    }

    private async Task AnalyzeDexScreenerFlagsAsync(LatestToken token, List<RiskFlag> flags, CancellationToken ct)
    {
        var address = token.Address?.Trim();
        if (string.IsNullOrWhiteSpace(address))
        {
            return;
        }

        // Hackathon scope: tokens are BNB chain contracts, mapped to DexScreener "bsc".
        var hasPaidVisibility = await _dexScreenerClient.HasPaidVisibilityAsync("bsc", address, ct);

        if (hasPaidVisibility)
        {
            AddFlagIfMissing(flags, new RiskFlag(
                BoostedVisibilityFlagCode,
                "Paid promotion detected",
                "Token has paid visibility on DexScreener (boost or paid profile detected).",
                15));
        }
    }

    private static void AddFlagIfMissing(List<RiskFlag> flags, RiskFlag flag)
    {
        if (!flags.Any(existing => string.Equals(existing.Code, flag.Code, StringComparison.Ordinal)))
        {
            flags.Add(flag);
        }
    }

    private static string? DetectSuspiciousNameReason(string name, string symbol)
    {
        var text = $"{name} {symbol}".Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var normalizedText = text.Trim();

        if (TryMatchPattern(normalizedText, SuspiciousScamActionPattern, out var actionMatch))
        {
            return $"matched action word: {actionMatch}";
        }

        if (ContainsUrl(normalizedText))
        {
            return "contains URL";
        }

        var specialCount = CountSuspiciousSpecialChars(normalizedText);
        if (specialCount >= 1)
        {
            return $"too many special chars: {specialCount}";
        }

        return null;
    }

    private static bool TryMatchPattern(string text, string pattern, out string matchValue)
    {
        matchValue = string.Empty;

        var match = Regex.Match(text, pattern, SuspiciousRegexOptions);
        if (match.Success)
        {
            matchValue = match.Value;
            return true;
        }

        return false;
    }

    private static bool ContainsUrl(string text)
        => Regex.IsMatch(text, @"(https?://|www\.|\.com\b|\.io\b|\.net\b)", SuspiciousRegexOptions);

    private static bool ContainsDescriptionExternalLink(string text)
        => Regex.IsMatch(text, @"(https?://|www\.|\.com\b|\.io\b|\.net\b|t\.me/|x\.com/)", SuspiciousRegexOptions);

    private static string? GetEffectiveDescription(LatestToken token)
    {
        if (!string.IsNullOrWhiteSpace(token.Description))
        {
            return token.Description.Trim();
        }

        if (!string.IsNullOrWhiteSpace(token.Desc))
        {
            return token.Desc.Trim();
        }

        // Four.Meme often stores description in Extra["descr"].
        if (token.Extra is not null &&
            token.Extra.TryGetValue("descr", out var descr) &&
            !string.IsNullOrWhiteSpace(descr))
        {
            return descr.Trim();
        }

        return null;
    }

    private static int CountSuspiciousSpecialChars(string text)
        => text.Count(ch => SuspiciousSpecialChars.Contains(ch));

    private static RiskFlag CreateFlag(string code, string reason)
        => code switch
        {
            "NAME_SUS" => new RiskFlag("NAME_SUS", "Suspicious name", reason, 10),
            NoSocialsFlagCode => new RiskFlag(NoSocialsFlagCode, "No socials", reason, 5),
            MetadataChangedFlagCode => new RiskFlag(MetadataChangedFlagCode, "Metadata changed", reason, 5),
            EmptyDescriptionFlagCode => new RiskFlag(EmptyDescriptionFlagCode, "Empty description", reason, 5),
            LinkInDescriptionFlagCode => new RiskFlag(LinkInDescriptionFlagCode, "External link in description", reason, 10),
            _ => new RiskFlag(code, code, reason, 0)
        };

    private void LogSuspiciousHeuristicExamples()
    {
        if (_heuristicExamplesLogged)
        {
            return;
        }

        _heuristicExamplesLogged = true;

        var examples = new (string Name, string Symbol)[]
        {
            ("Toshi", "TOSHI"),
            ("GMon", "GMON"),
            ("GitBadak", "GB"),
            ("Free Airdrop", "FAC"),
            ("SuperToken www.scam.com", "ST")
        };

        foreach (var sample in examples)
        {
            var reason = DetectSuspiciousNameReason(sample.Name, sample.Symbol);
            _logger.LogInformation("Suspicious-name heuristic sample '{Name}'/'{Symbol}' => {Result}", sample.Name, sample.Symbol, reason ?? "null");
        }
    }

    private static string NormalizeTokenName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var raw = name.Trim().ToLowerInvariant();
        var filtered = raw.Where(ch => !char.IsWhiteSpace(ch) && ch != '-' && ch != '_');
        return new string(filtered.ToArray());
    }

    private static string NormalizeAddress(string? address)
        => (address ?? string.Empty).Trim().ToLowerInvariant();
}
