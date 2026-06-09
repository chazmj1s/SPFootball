namespace SaturdayPulse.Models
{
    /// <summary>
    /// Lightweight POCO for deserializing the powerrankings/v2 API response.
    /// No INotifyPropertyChanged, no computed properties, no chart data.
    /// Maps to TeamRanking via TeamRankingMappingExtensions.ToTeamRanking().
    /// Property names match PowerRankingRowResponse fields exactly.
    /// </summary>
    public class TeamRankingDto
    {
        public int     TeamID           { get; set; }
        public string? TeamName         { get; set; }
        public string? Conference        { get; set; }
        public string? ConferenceAbbr   { get; set; }
        public string? Division         { get; set; }
        public string? Tier             { get; set; }
        public int     Year             { get; set; }
        public byte    Wins             { get; set; }
        public byte    Losses           { get; set; }
        public int     OverallRank      { get; set; }
        public int     TierRank         { get; set; }
        public double? Ranking          { get; set; }
        public double? PowerRating      { get; set; }
        public double? BaseSOS          { get; set; }
        public double? CombinedSOS      { get; set; }
        public double  AvgPointsScored  { get; set; }
        public double  AvgPointsAllowed { get; set; }
        public double  OffensiveZScore  { get; set; }
        public double  DefensiveZScore  { get; set; }
        public int     OffensiveRank    { get; set; }
        public int     DefensiveRank    { get; set; }
        public double? TrendRating      { get; set; }
        public double? PedigreeRating   { get; set; }
        public double? SeedRating       { get; set; }
        public List<double>? TrendHistory   { get; set; }
        public List<double>? PedigreeHistory { get; set; }
    }
}
