using SaturdayPulse.ModelViews;

namespace SaturdayPulse.Contracts.Responses
{
    public record TeamRecordsQueryResult(int Count, object Filters, IReadOnlyList<object> Results);
    public record RollingAveragesResult(int Year, int TeamCount, IReadOnlyList<object> Rankings);
    public record TeamRollingAveragesResult(int TeamId, string TeamName, string? Conference, IReadOnlyList<object> History);
    public record RivalriesResult(int TotalMatchups, int TotalInDatabase, object Filters, IReadOnlyList<object> Rivalries);
    public record PowerRankingsResult(bool IsWeekly, IReadOnlyList<PowerRankingRowResponse> Rankings);
    public record ScheduleResult(IReadOnlyList<object> Games);
    public record TeamsResult(IReadOnlyList<object> Teams);
    public record NamedRivalriesResult(IReadOnlyList<object> Rivalries);
    public record TeamHistoryResult(int TeamId, string TeamName, string ShortName, string? ConferenceAbbr, IReadOnlyList<object> History);
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
}