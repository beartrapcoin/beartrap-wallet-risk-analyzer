namespace BearTrap.Hackathon.Domain;

/// <summary>
/// View model for displaying risk flags in a user-friendly format.
/// </summary>
public sealed record FlagViewModel(
    string Code,
    string Label,
    string Severity,
    int Points
);
