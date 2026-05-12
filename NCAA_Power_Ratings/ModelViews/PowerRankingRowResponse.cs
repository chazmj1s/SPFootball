namespace NCAA_Power_Ratings.ModelViews
{
    public class PowerRankingRowResponse
    {
        int TeamID { get; set; }
        string TeamName { get; set; }
        string? Conference { get; set; }
        string? ConferenceAbbr { get; set; }
        string? Division { get; set; }
        string? Tier { get; set; }

        int OverallRank { get; set; }
        int TierRank { get; set; }

        decimal? Ranking { get; set; }

        int Year { get; set; }
        int Wins { get; set; }
        int Losses { get; set; }

        decimal? BaseSOS { get; set; }
        decimal? CombinedSOS { get; set; }

        decimal AvgPointsScored { get; set; }
        decimal AvgPointsAllowed { get; set; }

        int OffensiveRank { get; set; }
        int DefensiveRank { get; set; }

        double TrendRating { get; set; }
        double PedigreeRating { get; set; }
        double SeedRating { get; set; }

        IReadOnlyList<double>? TrendHistory { get; set; }
        IReadOnlyList<double>? PedigreeHistory { get; set; }
    }

}
