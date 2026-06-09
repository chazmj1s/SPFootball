namespace SaturdayPulse.Models
{
    /// <summary>
    /// Lightweight POCO for deserializing the schedule/v2 API response.
    /// No INotifyPropertyChanged, no computed properties, no string formatting.
    /// Maps to GameResult via GameResultMappingExtensions.ToGameResult().
    /// </summary>
    public class GameResultDto
    {
        public int     Id          { get; set; }
        public int     Year        { get; set; }
        public int     Week        { get; set; }
        public string? GameDate    { get; set; }
        public string? GameDay     { get; set; }
        public string  SeasonType  { get; set; } = "regular";

        // Home
        public string  HomeName      { get; set; } = string.Empty;
        public int     HomeId        { get; set; }
        public string  HomeConf      { get; set; } = string.Empty;
        public string  HomeTier      { get; set; } = string.Empty;
        public int     HomePoints    { get; set; }
        public double? HomeProjScore { get; set; }

        // Away
        public string  AwayName      { get; set; } = string.Empty;
        public int     AwayId        { get; set; }
        public string  AwayConf      { get; set; } = string.Empty;
        public string  AwayTier      { get; set; } = string.Empty;
        public int     AwayPoints    { get; set; }
        public double? AwayProjScore { get; set; }

        public char    Location  { get; set; }
        public bool    IsPlayed  { get; set; }
        public int     ActualOU  { get; set; }
        public double? ProjOU    { get; set; }

        // Nested stats — deserialized as raw objects, mapped separately
        public GameTeamStatsDto? HomeStats  { get; set; }
        public GameTeamStatsDto? AwayStats  { get; set; }
        public GameLinesDto?     VegasLines { get; set; }
    }

    public class GameTeamStatsDto
    {
        public int     TeamId           { get; set; }
        public string? TeamName         { get; set; }
        public int     OverallRank      { get; set; }
        public string? Record           { get; set; }
        public double? PowerRating      { get; set; }
        public double? CombinedSOS      { get; set; }
        public int?    OffensiveRank    { get; set; }
        public double? AvgPointsScored  { get; set; }
        public double? OffensiveZScore  { get; set; }
        public int?    DefensiveRank    { get; set; }
        public double? AvgPointsAllowed { get; set; }
        public double? DefensiveZScore  { get; set; }
    }

    public class GameLinesDto
    {
        public decimal? Spread        { get; set; }
        public decimal? SpreadOpen    { get; set; }
        public decimal? OverUnder     { get; set; }
        public decimal? OverUnderOpen { get; set; }
        public int?     HomeMoneyline { get; set; }
        public int?     AwayMoneyline { get; set; }
        public int      ProviderCount { get; set; }
    }
}
