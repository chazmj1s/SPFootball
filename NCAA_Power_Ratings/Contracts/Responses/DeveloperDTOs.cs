namespace NCAA_Power_Ratings.Contracts.Responses
{
    public record BackfillResult(string Message, int Processed, int? StartYear);
    public record AnalyticsResult(int TotalRecords, string YearRange, IEnumerable<object> Overperformers, IEnumerable<object> Underperformers, double? AveragePowerRating, double? AverageSOS);
    public record TeamGameAnalysisResult(int TeamId, int Year, string Record, decimal? CombinedSOS, double AvgZScore, decimal? PowerRating, double CalculatedPowerRating, IEnumerable<object> Games);
    public record TrendsResult(int Year, int TeamCount, IEnumerable<object> Trends);
    public record DiagnosticScoreDeltaResult(int Year, int TotalGames, int UpsetCount, int NegativeDeltas, bool ShouldHaveNegatives, string? Problem, IEnumerable<object> SampleGames);
    public record AvailableFilesResult(string Directory, int FileCount, IEnumerable<object> Files);
    public record RecalculateScoreDeltasResult(string Message, int BucketsCreated, string BucketSystem, string NextStep);
    public record RecreateTableResult(string Message, int BucketsCreated, string Action);
    public record MatchupHistoriesResult(string Message, int MatchupsCreated, int RivalriesProcessed, string NextStep);
    public record WeeklyRankingsBackfillResult(string Message, int Processed, int? StartYear);
    public record ComputeWeeklyResult(string Message, int? Year, int? Week);
}