namespace BearTrap.Hackathon.Services.DataSources;

public sealed record TokenDto(
    string Address,
    string Name,
    string Symbol,
    string Creator,
    DateTimeOffset CreatedAt,
    string? ImageUrl,
    decimal? ProgressPercent,
    string? CreatorUserId = null,
    string? WebUrl = null,
    string? TelegramUrl = null,
    string? TwitterUrl = null,
    DateTimeOffset? ModifiedAt = null
);
