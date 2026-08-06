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
        public double? ProjOU { get; set; }
        public double? ProjMargin { get; set; }

        // Nested stats — deserialized as raw objects, mapped separately
        public GameTeamStatsDto? HomeStats    { get; set; }
        public GameTeamStatsDto? AwayStats    { get; set; }
        public GameLinesDto?     VegasLines   { get; set; }
        public RivalryNotesDto?  RivalryNotes { get; set; }
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

    /// <summary>
    /// Maps 1:1 onto the "RivalryNotes" object added to GetScheduleV2Async /
    /// GetTeamScheduleV2Async's response (null for non-curated pairings).
    /// Field names match ProductionGameDataService's BuildRivalryNotes output
    /// exactly: RivalryName, FirstPlayed, AverageSpread, AverageOverUnder,
    /// UpsetChance, Blurb.
    /// </summary>
    public class RivalryNotesDto
    {
        public string? RivalryName      { get; set; }
        public int      FirstPlayed      { get; set; }
        public double   AverageSpread    { get; set; }
        public double   AverageOverUnder { get; set; }
        public double   UpsetChance      { get; set; }
        public string?  Blurb            { get; set; }
    }
}
