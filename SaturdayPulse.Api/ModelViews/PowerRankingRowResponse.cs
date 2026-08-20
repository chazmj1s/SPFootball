public class PowerRankingRowResponse
{
    public int TeamID { get; set; }

    public string? TeamName { get; set; }
    public string? Conference { get; set; }
    public string? ConferenceAbbr { get; set; }
    public string? Division { get; set; }
    public string? Tier { get; set; }

    public int? OverallRank { get; set; }
    public int? TierRank { get; set; }

    public double? Ranking { get; set; }
    public double? PowerRating { get; set; }

    public int Year { get; set; }

    public int Wins { get; set; }
    public int Losses { get; set; }

    /// <summary>
    /// Real wins/losses so far, plus predicted W/L for each remaining game on
    /// the schedule (from the existing Projections snapshot). See
    /// ProductionGameDataService.V2.GetPowerRankingsV2Async /
    /// BuildProjectedRecordRollup. Added 2026-08 for the Rankings/MyTeam
    /// season-projection feature.
    /// </summary>
    public int ProjectedWins { get; set; }
    public int ProjectedLosses { get; set; }

    public double? BaseSOS { get; set; }
    public double? CombinedSOS { get; set; }

    public double? AvgPointsScored { get; set; }
    public double? AvgPointsAllowed { get; set; }

    public double? OffensiveZScore { get; set; }
    public double? DefensiveZScore { get; set; }

    public int? OffensiveRank { get; set; }
    public int? DefensiveRank { get; set; }

    /// <summary>
    /// National ordinal rank (1 = best) of this team's TeamRecord.ZRoster among all
    /// FBS teams with a computed ZRoster for the year. Null if ZRoster hasn't been
    /// computed for this team/year (RosterCapacityService.ComputeZRosterAsync not
    /// yet run) — do NOT render as "unranked last," treat as "no roster data."
    /// Computed at request time in GetPowerRankingsV2Async; ZRoster itself is the
    /// persisted source of truth, this is a derived sort position only.
    /// </summary>
    public int? RosterRank { get; set; }

    public double? TrendRating { get; set; }
    public double? PedigreeRating { get; set; }
    public double? SeedRating { get; set; }

    public List<double>? TrendHistory { get; set; }
    public List<double>? PedigreeHistory { get; set; }
}
