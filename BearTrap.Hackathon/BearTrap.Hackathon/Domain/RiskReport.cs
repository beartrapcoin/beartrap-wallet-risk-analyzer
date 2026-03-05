namespace BearTrap.Hackathon.Domain;

public sealed record RiskReport(int Score, List<RiskFlag> Flags, List<string> DescriptionLinks)
{
    /// <summary>
    /// Get user-friendly display flags with labels and severities.
    /// </summary>
    public IEnumerable<FlagViewModel> DisplayFlags => RiskFlags.GetDisplayFlags(this);
}
