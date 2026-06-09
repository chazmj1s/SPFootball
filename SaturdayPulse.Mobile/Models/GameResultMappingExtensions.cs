namespace SaturdayPulse.Models
{
    /// <summary>
    /// Extension methods for mapping DTOs to UI-bound models.
    /// All string formatting, splitting, and computed properties
    /// are resolved here on a background thread before the
    /// ObservableCollection is populated on the main thread.
    /// </summary>
    public static class GameResultMappingExtensions
    {
        public static GameResult ToGameResult(this GameResultDto dto)
        {
            return new GameResult
            {
                Id            = dto.Id,
                Year          = dto.Year,
                Week          = dto.Week,
                GameDate      = dto.GameDate,
                GameDay       = dto.GameDay,
                SeasonType    = dto.SeasonType,

                HomeName      = dto.HomeName,
                HomeId        = dto.HomeId,
                HomeConf      = dto.HomeConf,
                HomeTier      = dto.HomeTier,
                HomePoints    = dto.HomePoints,
                HomeProjScore = dto.HomeProjScore,

                AwayName      = dto.AwayName,
                AwayId        = dto.AwayId,
                AwayConf      = dto.AwayConf,
                AwayTier      = dto.AwayTier,
                AwayPoints    = dto.AwayPoints,
                AwayProjScore = dto.AwayProjScore,

                Location      = dto.Location,
                IsPlayed      = dto.IsPlayed,
                ActualOU      = dto.ActualOU,
                ProjOU        = dto.ProjOU,

                HomeStats     = dto.HomeStats?.ToGameTeamStats(),
                AwayStats     = dto.AwayStats?.ToGameTeamStats(),
                VegasLines    = dto.VegasLines?.ToGameLines(),
            };
        }

        public static List<GameResult> ToGameResults(this IEnumerable<GameResultDto> dtos)
            => dtos.Select(d => d.ToGameResult()).ToList();

        private static GameTeamStats ToGameTeamStats(this GameTeamStatsDto dto)
            => new()
            {
                TeamId           = dto.TeamId,
                TeamName         = dto.TeamName ?? string.Empty,
                OverallRank      = dto.OverallRank,
                Record           = dto.Record ?? string.Empty,
                PowerRating      = dto.PowerRating,
                CombinedSOS      = dto.CombinedSOS,
                OffensiveRank    = dto.OffensiveRank ?? 0,
                AvgPointsScored  = dto.AvgPointsScored,
                OffensiveZScore  = dto.OffensiveZScore,
                DefensiveRank    = dto.DefensiveRank ?? 0,
                AvgPointsAllowed = dto.AvgPointsAllowed,
                DefensiveZScore  = dto.DefensiveZScore,
            };

        private static GameLines ToGameLines(this GameLinesDto dto)
            => new()
            {
                Spread        = dto.Spread,
                SpreadOpen    = dto.SpreadOpen,
                OverUnder     = dto.OverUnder,
                OverUnderOpen = dto.OverUnderOpen,
                HomeMoneyline = dto.HomeMoneyline,
                AwayMoneyline = dto.AwayMoneyline,
                ProviderCount = dto.ProviderCount,
            };
    }
}
