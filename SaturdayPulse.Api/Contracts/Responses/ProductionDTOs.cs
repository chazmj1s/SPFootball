using SaturdayPulse.ModelViews;

namespace SaturdayPulse.Contracts.Responses
{
    public record TeamRecordsQueryResult(int Count, object Filters, IReadOnlyList<object> Results);
    public record RollingAveragesResult(int Year, int TeamCount, IReadOnlyList<object> Rankings);
    public record TeamRollingAveragesResult(int TeamId, string TeamName, string? Conference, IReadOnlyList<object> History);
    public record RivalriesResult(int TotalMatchups, int TotalInDatabase, object Filters, IReadOnlyList<object> Rivalries);
    public record PowerRankingsResult(bool IsWeekly, IReadOnlyList<PowerRankingRowResponse> Rankings);
    public record ScheduleResult(IReadOnlyList<object> Games);
    public record PlayedWeekDto(int Week, string? GameDate);
    public record TeamsResult(IReadOnlyList<object> Teams);
    public record NamedRivalriesResult(IReadOnlyList<object> Rivalries);
    public record TeamHistoryResult(int TeamId, string TeamName, string ShortName, string? ConferenceAbbr, IReadOnlyList<object> History);
    public record TeamSeasonArcResult(int TeamId, string TeamName, int Year, IReadOnlyList<object> Weeks);
    public record DiagnosticInfo(
        string Database,
        int TotalTeams,
        int TotalGames,
        int TotalRecords,
        int RecordsWithPowerRating,
        IReadOnlyList<object> YearsWithData,
        IReadOnlyList<object> YearStats);

    public record RivalryHistoryResult(
        int Team1Id, string Team1Name, string Team1ShortName,
        int Team2Id, string Team2Name, string Team2ShortName,
        string? RivalryName, string? RivalryTier,
        int GamesPlayed, decimal? AvgMargin, decimal? UpsetRate,
        IReadOnlyList<object> History, object? CurrentYearProjection);

    public record ChampionshipQualifiersResult(IReadOnlyList<object> Conferences);
    public record TeamScheduleV2Result(object? Summary, IReadOnlyList<object> Games);

    /// <summary>
    /// AverageTotalPoints added — it already existed on the AvgScoreDifferential entity
    /// (populated in the DB from the same 60-year historical build as AverageMargin/
    /// StdDevMargin) but was never mapped into this record, so GamePredictionService
    /// had no way to use the bucket's own historical total scoring; it built total
    /// points entirely from PPG/PAG averaging instead. Now surfaced so
    /// GamePredictionService can anchor total points on real historical data for this
    /// exact strength differential, with team-specific PPG/PAG as a reliability-
    /// weighted corroboration adjustment rather than the sole source.
    ///
    /// Default value of 0.0 preserves source compatibility with any positional-arg
    /// construction elsewhere that predates this field.
    /// </summary>
    public record ExpectedGameDistribution(
        double ExpectedMargin,
        double StdDev,
        double Reliability,
        int SampleSize,
        double AverageTotalPoints = 0.0);

}
