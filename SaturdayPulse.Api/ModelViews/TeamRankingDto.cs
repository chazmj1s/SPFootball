namespace SaturdayPulse.ModelViews
{
    /// <summary>
    /// DTO for team power rankings returned by the API
    /// </summary>
    public class TeamRankingDto
    {
        public int TeamID { get; set; }
        public string TeamName { get; set; } = string.Empty;
        public string? Conference { get; set; }
        public string? ConferenceAbbr { get; set; }
        public string? Division { get; set; }
        public int Rank { get; set; }
        public decimal PowerRating { get; set; }
        public int Year { get; set; }
        public int Wins { get; set; }
        public int Losses { get; set; }
        public decimal? BaseSOS { get; set; }
        public decimal? CombinedSOS { get; set; }
    }
}
