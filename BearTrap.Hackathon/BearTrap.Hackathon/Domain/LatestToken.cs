namespace BearTrap.Hackathon.Domain;

public sealed record LatestToken(
    string Address,
    string Name,
    string Symbol,
    string Creator,
    DateTimeOffset CreatedAt,
    string? ImageUrl = null,
    decimal? ProgressPercent = null,
    string? CreatorUserId = null,
    string? WebUrl = null,
    string? TelegramUrl = null,
    string? TwitterUrl = null,
    DateTimeOffset? ModifiedAt = null,
    string? Description = null
);
