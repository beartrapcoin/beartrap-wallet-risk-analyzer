using BearTrap.Hackathon.Application.Abstractions;
using BearTrap.Hackathon.Data;
using BearTrap.Hackathon.Data.Entities;
using BearTrap.Hackathon.Domain;
using BearTrap.Hackathon.Services.DataSources;
using Microsoft.EntityFrameworkCore;
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

    // Generic scam keywords for NAME_TOO_GENERIC flag
    private static readonly string[] GenericScamKeywords = new[]
    {
        "token", "coin", "bnb", "moon", "inu", "ai", "100x", "pump", "profit", "doge", "safe", "elon"
    };

    public RiskAnalyzer(
        AppDbContext db,
        IFourMemeSource fourMemeSource,
        IBnbRpcClient bnbRpcClient)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _fourMemeSource = fourMemeSource ?? throw new ArgumentNullException(nameof(fourMemeSource));
        _bnbRpcClient = bnbRpcClient ?? throw new ArgumentNullException(nameof(bnbRpcClient));
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
            .GroupBy(t => t.Name.Trim().ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.Count());

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
            AnalyzeBatchFlags(token, creatorCounts, nameCounts, flags);

            // Database-based flags
            await AnalyzeDatabaseFlagsAsync(token, flags, ct);

            // Pattern-based flags
            AnalyzePatternFlags(token, flags);

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
        List<RiskFlag> flags)
    {
        // CREATOR_BURST (+30): Same creator appears >= 3 times in batch
        var creatorKey = token.Creator?.Trim().ToLowerInvariant() ?? "";
        if (!string.IsNullOrWhiteSpace(creatorKey) && creatorCounts.TryGetValue(creatorKey, out var creatorCount))
        {
            if (creatorCount >= 3)
            {
                flags.Add(new RiskFlag(
                    "CREATOR_BURST",
                    "Creator burst deploys",
                    $"Creator deployed {creatorCount} tokens in this batch.",
                    30));
            }
        }

        // DUPLICATE_NAME_IN_BATCH (+20): Name appears > 1 time in batch
        var nameKey = token.Name?.Trim().ToLowerInvariant() ?? "";
        if (!string.IsNullOrWhiteSpace(nameKey) && nameCounts.TryGetValue(nameKey, out var nameCount))
        {
            if (nameCount > 1)
            {
                flags.Add(new RiskFlag(
                    "DUPLICATE_NAME_IN_BATCH",
                    "Duplicate name in batch",
                    $"Name '{token.Name}' appears {nameCount} times in this batch.",
                    20));
            }
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
            flags.Add(new RiskFlag(
                "CREATOR_SPAM",
                "Creator spam",
                $"Creator {token.Creator} created {creatorCount} tokens in the last 24 hours.",
                35));
        }

        // NAME_REUSED (+15): Exact name or symbol match in database
        var reused = await _db.TokenSnapshots
            .AnyAsync(x =>
                x.Address != token.Address &&
                (x.Name == token.Name || x.Symbol == token.Symbol), ct);

        if (reused)
        {
            flags.Add(new RiskFlag(
                "NAME_REUSED",
                "Name or symbol reused",
                "Another token with the same name or symbol exists in local history.",
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

        // SYMBOL_TOO_SHORT (+10): Symbol length <= 2
        if (symbol.Length > 0 && symbol.Length <= 2)
        {
            flags.Add(new RiskFlag(
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
                flags.Add(new RiskFlag(
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
                flags.Add(new RiskFlag(
                    "NAME_TOO_GENERIC",
                    "Generic hype name",
                    $"Name contains generic scam keywords: {string.Join(", ", matchedKeywords)}.",
                    15));
            }
        }

        // NAME_SUS (+20): Suspicious name/symbol patterns (existing logic)
        var nameUpper2 = name.ToUpperInvariant();
        var symbolUpper = symbol.ToUpperInvariant();
        var suspiciousKeywords = new[] { "BNB", "BINANCE", "USDT", "ETH" };

        var containsSuspicious = suspiciousKeywords.Any(k => nameUpper2.Contains(k) || symbolUpper.Contains(k));
        var shortSymbol = symbol.Length > 0 && symbol.Length <= 2;
        var symbolHasDigitsAndShort = symbol.Any(char.IsDigit) && symbol.Length <= 5;

        if (containsSuspicious || shortSymbol || symbolHasDigitsAndShort)
        {
            flags.Add(new RiskFlag(
                "NAME_SUS",
                "Suspicious name or symbol",
                "Name or symbol contains popular token names, is very short, or contains digits with short length.",
                20));
        }
    }
}
