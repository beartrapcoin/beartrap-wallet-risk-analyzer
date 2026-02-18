namespace BearTrap.Hackathon.Services.DataSources;

public sealed record TokenDto(
    string Address,
    string Name,
    string Symbol,
    string Creator,
    DateTimeOffset CreatedAt,
    string? ImageUrl
);
