namespace SaturdayPulse.Models

{
    /// <summary>
    /// Extension methods for mapping TeamRankingDto to TeamRanking.
    /// Chart data structures (ObservableCollection, ChartPoints) are
    /// NOT populated here — they are lazy-loaded when the user expands
    /// the trend/arc panels. This keeps the mapping fast.
    /// </summary>
    public static class TeamRankingMappingExtensions
    {
        public static TeamRanking ToTeamRanking(this TeamRankingDto dto)
        {
            var ranking = new TeamRanking
            {
                TeamID           = dto.TeamID,
                TeamName         = dto.TeamName ?? string.Empty,
                Conference       = dto.Conference,
                ConferenceAbbr   = dto.ConferenceAbbr,
                Division         = dto.Division,
                Tier             = dto.Tier,
                Year             = dto.Year,
                Wins             = dto.Wins,
                Losses           = dto.Losses,
                OverallRank      = dto.OverallRank,
                TierRank         = dto.TierRank,
                Ranking          = dto.Ranking.HasValue ? (decimal?)Convert.ToDecimal(dto.Ranking) : null,
                BaseSOS          = dto.BaseSOS.HasValue ? (decimal?)Convert.ToDecimal(dto.BaseSOS) : null,
                CombinedSOS      = dto.CombinedSOS.HasValue ? (decimal?)Convert.ToDecimal(dto.CombinedSOS) : null,
                AvgPointsScored  = Convert.ToDecimal(dto.AvgPointsScored),
                AvgPointsAllowed = Convert.ToDecimal(dto.AvgPointsAllowed),
                OffensiveZScore  = Convert.ToDecimal(dto.OffensiveZScore),
                DefensiveZScore  = Convert.ToDecimal(dto.DefensiveZScore),
                OffensiveRank    = dto.OffensiveRank,
                DefensiveRank    = dto.DefensiveRank,
                TrendRating      = dto.TrendRating,
                PedigreeRating   = dto.PedigreeRating,
                SeedRating       = dto.SeedRating,
                TrendHistory     = dto.TrendHistory,
                PedigreeHistory  = dto.PedigreeHistory,
            };

            return ranking;
        }

        public static List<TeamRanking> ToTeamRankings(this IEnumerable<TeamRankingDto> dtos)
            => dtos.Select(d => d.ToTeamRanking()).ToList();
    }
}
