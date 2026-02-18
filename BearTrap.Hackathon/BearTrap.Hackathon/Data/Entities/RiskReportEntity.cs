namespace BearTrap.Hackathon.Data.Entities
{
    public class RiskReportEntity
    {
        public int Id { get; set; }
        public string TokenAddress { get; set; } = null!;
        public int Score { get; set; }
        public string FlagsJson { get; set; } = "[]";
        public string AiSummary { get; set; } = "";
        public DateTimeOffset AnalyzedAt { get; set; }
    }
}
