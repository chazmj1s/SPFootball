using SaturdayPulse.Models;

namespace SaturdayPulse.Api.Contracts.Responses
{
    public class GameProjectionData
    {
        public int GameId { get; set; }
        public int Year { get; set; }
        public int Week { get; set; }
        public int? HomePoints { get; set; }
        public int? AwayPoints { get; set; }
        public string? ConferenceTier { get; set; }
        public Projection BestProjection { get; set; } = null!;
        public decimal? VegasSpread { get; set; }  // consensus closing spread
        public decimal? VegasSpreadOpen { get; set; }  // consensus opening spread
        public decimal? VegasTotal { get; set; }  // consensus over/under
    }

    public record ProjectionAccuracyResult(
    int StartYear,
    int EndYear,
    int TotalGames,
    double MAE,
    double TotalMAE,
    double WinnerAccuracyPct,
    double SpreadBias,
    double TotalBias,
    double? VegasMAE,
    double? VegasWinnerAccuracy,
    int VegasGames,
    double? VegasOpenMAE,
    double? VegasOpenWinnerAccuracy,
    int VegasOpenGames,
    double? VegasTotalMAE,
    double? VegasTotalBias,
    List<AccuracyByWeek> ByWeek,
    List<AccuracyByYear> ByYear,
    List<AccuracyByConference> ByConference)
    {
        public static ProjectionAccuracyResult Empty(int? startYear, int? endYear) =>
            new(startYear ?? 0, endYear ?? 0, 0, 0, 0, 0, 0, 0,
                null, null, 0, null, null, 0, null, null,
                new(), new(), new());
    }

    public record AccuracyByWeek(
        int SnapshotWeek,
        int Games,
        double MAE,
        double WinnerAccuracy);

    public record AccuracyByYear(
        int Year,
        int Games,
        double MAE,
        double WinnerAccuracy,
        double SpreadBias);

    public record AccuracyByConference(
        string Conference,
        int Games,
        double MAE,
        double WinnerAccuracy);

}

