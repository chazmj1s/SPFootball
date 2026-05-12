namespace SaturdayPulse.ModelViews
{
    public class TeamGameResultView
    {
        public int Week { get; set; }
        public string Result { get; set; } = string.Empty;
        public string Opponent { get; set; } = string.Empty;
        public required string Division { get; set; }
        public required string Conference { get; set; }
        public string Score { get; set; } = string.Empty;
    }
}