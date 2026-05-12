namespace SaturdayPulse.ModelViews
{
    public class TeamSeasonSummaryView
    {
        public int Year { get; set; }
        public string TeamName { get; set; } = string.Empty;
        public byte Wins { get; set; }
        public byte Losses { get; set; }
        public int PointsFor { get; set; }
        public int PointsAgainst { get; set; }
    }
}