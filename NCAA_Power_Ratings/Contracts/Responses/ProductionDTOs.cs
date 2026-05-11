namespace NCAA_Power_Ratings.Contracts.Responses
{
    public record TeamRecordsQueryResult(int Count, object Filters, IReadOnlyList<object> Results);
    public record RollingAveragesResult(int Year, int TeamCount, IReadOnlyList<object> Rankings);
    public record TeamRollingAveragesResult(int TeamId, string TeamName, string? Conference, IReadOnlyList<object> History);
    public record RivalriesResult(int TotalMatchups, int TotalInDatabase, object Filters, IReadOnlyList<object> Rivalries);
    public record PowerRankingsResult(bool IsWeekly, IReadOnlyList<object> Rankings);
    public record ScheduleResult(IReadOnlyList<object> Games);
    public record TeamsResult(IReadOnlyList<object> Teams);
    public record NamedRivalriesResult(IReadOnlyList<object> Rivalries);
    public record TeamHistoryResult(int TeamId, string TeamName, string ShortName, string? ConferenceAbbr, IReadOnlyList<object> History);
}