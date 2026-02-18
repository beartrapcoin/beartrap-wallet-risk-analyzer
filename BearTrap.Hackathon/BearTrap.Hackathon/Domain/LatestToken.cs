namespace BearTrap.Hackathon.Domain;

public sealed record LatestToken(
    string Address,
    string Name,
    string Symbol,
    string Creator,
    DateTimeOffset CreatedAt,
    string? ImageUrl = null
);
