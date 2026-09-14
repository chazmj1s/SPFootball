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
                GameTime      = dto.GameTime,
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
                ProjMargin    = dto.ProjMargin,

                Status        = dto.Status,
                Period        = dto.Period,
                Clock         = dto.Clock,

                HomeLineScores = dto.HomeLineScores?.ToIntList() ?? new List<int>(),
                AwayLineScores = dto.AwayLineScores?.ToIntList() ?? new List<int>(),

                HomeStats = dto.HomeStats?.ToGameTeamStats(),
                AwayStats     = dto.AwayStats?.ToGameTeamStats(),
                VegasLines    = dto.VegasLines?.ToGameLines(),
                RivalryNotes  = dto.RivalryNotes?.ToRivalryNotes(),
            };
        }

        public static List<int> ToIntList(this string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return new List<int>();
            }

            // Split by comma and remove surrounding whitespace from each item
            var segments = input.Split(',', StringSplitOptions.RemoveEmptyEntries);

            return segments
                .Select(s => (Success: int.TryParse(s.Trim(), out int val), Value: val))
                .Where(pair => pair.Success)
                .Select(pair => pair.Value)
                .ToList();
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

        private static RivalryNotes ToRivalryNotes(this RivalryNotesDto dto)
            => new()
            {
                RivalryName      = dto.RivalryName ?? string.Empty,
                FirstPlayed      = dto.FirstPlayed,
                AverageSpread    = dto.AverageSpread,
                AverageOverUnder = dto.AverageOverUnder,
                UpsetChance      = dto.UpsetChance,
                Blurb            = dto.Blurb ?? string.Empty,
                Series           = dto.Series ?? string.Empty,
            };
    }
}
