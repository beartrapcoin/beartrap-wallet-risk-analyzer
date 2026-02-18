namespace BearTrap.Hackathon.Domain;

/// <summary>
/// Static definitions for all risk flags with their display metadata.
/// </summary>
public static class RiskFlags
{
    // Severity levels
    public const string SeverityLow = "low";
    public const string SeverityMedium = "medium";
    public const string SeverityHigh = "high";

    // Flag definitions: (Code, Label, Severity, Points)
    private static readonly Dictionary<string, (string Label, string Severity, int Points)> _flagDefinitions = new()
    {
        // Existing flags
        ["CREATOR_SPAM"] = ("Creator burst deploys", SeverityHigh, 35),
        ["NAME_SUS"] = ("Suspicious name", SeverityMedium, 20),
        ["NAME_REUSED"] = ("Name reused", SeverityMedium, 15),

        // New batch-based flags
        ["CREATOR_BURST"] = ("Creator burst", SeverityHigh, 30),
        ["DUPLICATE_NAME_IN_BATCH"] = ("Duplicate name", SeverityMedium, 20),
        ["SYMBOL_TOO_SHORT"] = ("Short symbol", SeverityLow, 10),
        ["SYMBOL_RANDOMIZED"] = ("Random symbol", SeverityMedium, 15),
        ["NAME_TOO_GENERIC"] = ("Generic hype", SeverityMedium, 15)
    };

    /// <summary>
    /// Convert a RiskFlag to a FlagViewModel with friendly display properties.
    /// </summary>
    public static FlagViewModel ToViewModel(RiskFlag flag)
    {
        if (_flagDefinitions.TryGetValue(flag.Code, out var def))
        {
            return new FlagViewModel(flag.Code, def.Label, def.Severity, def.Points);
        }

        // Fallback for unknown flags
        return new FlagViewModel(flag.Code, flag.Title, SeverityLow, flag.Points);
    }

    /// <summary>
    /// Get all display flags for a risk report.
    /// </summary>
    public static IEnumerable<FlagViewModel> GetDisplayFlags(RiskReport report)
    {
        return report.Flags.Select(ToViewModel);
    }
}
