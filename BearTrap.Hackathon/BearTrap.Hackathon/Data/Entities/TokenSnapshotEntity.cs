namespace BearTrap.Hackathon.Data.Entities
{
    public class TokenSnapshotEntity
    {
        public int Id { get; set; }
        public string Address { get; set; } = null!;
        public string Name { get; set; } = "";
        public string Symbol { get; set; } = "";
        public string Creator { get; set; } = "";
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset FirstSeenAt { get; set; }
    }
}
