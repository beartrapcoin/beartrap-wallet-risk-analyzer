namespace BearTrap.Hackathon.Data.Entities
{
    public class TokenSnapshotEntity
    {
        public int Id { get; set; }
        public string Address { get; set; } = null!;
        public string Name { get; set; } = "";
        public string Symbol { get; set; } = "";
        public string Creator { get; set; } = "";
        public string? ImageKey { get; set; }
        public string? WebUrl { get; set; }
        public string? TelegramUrl { get; set; }
        public string? TwitterUrl { get; set; }
        public string? Description { get; set; }
        public int SnapshotCount { get; set; }
        public DateTimeOffset? LastObservedAt { get; set; }
        public int ImageChangeCount24h { get; set; }
        public DateTimeOffset? ImageChangeWindowStartedAt { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset FirstSeenAt { get; set; }
    }
}
