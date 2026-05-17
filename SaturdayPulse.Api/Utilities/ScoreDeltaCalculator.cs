using SaturdayPulse.Contracts;
using SaturdayPulse.Models;

namespace SaturdayPulse.Utilities
{
    public class ScoreDeltaCalculator(IUnitOfWork _uow)
    {
        public async Task<List<AvgScoreDeltaStats>> CalculateAvgScoreDeltasAsync(CancellationToken token = default)
        {
            // Load all played games and team records into memory
            var games      = await _uow.Game.GetPlayedGamesSinceYearAsync(1, token);
            var allRecords = await _uow.TeamRecords.GetHistoricalAsync(1, 9999, token);

            // Build win percentage lookup: (teamId, year) → record
            var recordLookup = allRecords
                .GroupBy(r => (r.TeamID, (int)r.Year))
                .ToDictionary(g => g.Key, g => g.First());

            // Join games with team records to get win percentages
            var cteData = games
                .Select(g =>
                {
                    recordLookup.TryGetValue((g.WinnerId, g.Year), out var winnerRecord);
                    recordLookup.TryGetValue((g.LoserId, g.Year), out var loserRecord);

                    var winnerGames = winnerRecord != null ? winnerRecord.Wins + winnerRecord.Losses : 0;
                    var loserGames  = loserRecord  != null ? loserRecord.Wins  + loserRecord.Losses  : 0;

                    return new
                    {
                        WinnerWinPct = winnerGames > 0
                            ? (decimal)winnerRecord!.Wins / winnerGames
                            : 0m,
                        LoserWinPct = loserGames > 0
                            ? (decimal)loserRecord!.Wins / loserGames
                            : 0m,
                        WinnerPoints = g.WPoints,
                        LoserPoints  = g.LPoints
                    };
                })
                .ToList();

            // Round to 0.05 increments (5% buckets) and normalize so Team1WinPct >= Team2WinPct
            var normalizedData = cteData
                .Select(x => new
                {
                    WinPct1 = Math.Round(x.WinnerWinPct * 20m, MidpointRounding.AwayFromZero) / 20m,
                    WinPct2 = Math.Round(x.LoserWinPct  * 20m, MidpointRounding.AwayFromZero) / 20m,
                    x.WinnerPoints,
                    x.LoserPoints
                })
                .Select(x => new
                {
                    Team1WinPct = x.WinPct1 >= x.WinPct2 ? x.WinPct1 : x.WinPct2,
                    Team2WinPct = x.WinPct1 >= x.WinPct2 ? x.WinPct2 : x.WinPct1,
                    Delta       = x.WinPct1 >= x.WinPct2
                        ? x.WinnerPoints - x.LoserPoints
                        : x.LoserPoints  - x.WinnerPoints
                })
                .ToList();

            // Group by (Team1WinPct, Team2WinPct) and calculate statistics
            var results = normalizedData
                .GroupBy(x => new { x.Team1WinPct, x.Team2WinPct })
                .Select(g =>
                {
                    var deltas = g.Select(x => (double)x.Delta).ToList();
                    return new AvgScoreDeltaStats
                    {
                        Team1WinPct = g.Key.Team1WinPct,
                        Team2WinPct = g.Key.Team2WinPct,
                        AvgDelta    = Math.Round(deltas.Average(), 2),
                        StdDevP     = CalculateStandardDeviationPopulation(deltas),
                        SampleSize  = g.Count()
                    };
                })
                .OrderByDescending(x => x.Team1WinPct)
                .ThenByDescending(x => x.Team2WinPct)
                .ToList();

            return results;
        }

        /// <summary>
        /// Calculates population standard deviation (STDEVP equivalent).
        /// </summary>
        private static double CalculateStandardDeviationPopulation(IEnumerable<double> values)
        {
            var valuesList = values.ToList();
            if (valuesList.Count == 0) return 0.0;

            var mean = valuesList.Average();
            var sumOfSquaredDifferences = valuesList.Sum(v => Math.Pow(v - mean, 2));
            return Math.Sqrt(sumOfSquaredDifferences / valuesList.Count);
        }

        /// <summary>
        /// Clears and repopulates the AvgScoreDeltas table.
        /// </summary>
        public async Task UpdateAvgScoreDeltasTableAsync(CancellationToken token = default)
        {
            var stats = await CalculateAvgScoreDeltasAsync(token);

            await _uow.Lookups.ClearAvgScoreDeltasAsync(token);

            await _uow.Lookups.AddAvgScoreDeltasAsync(stats.Select(stat => new AvgScoreDelta
            {
                Team1WinPct       = stat.Team1WinPct,
                Team2WinPct       = stat.Team2WinPct,
                AverageScoreDelta = (decimal)Math.Round(stat.AvgDelta, 2),
                StDevP            = (decimal)Math.Round(stat.StdDevP, 8),
                SampleSize        = stat.SampleSize
            }), token);

            await _uow.SaveChangesAsync(token);
        }
    }

    /// <summary>
    /// DTO for average score delta statistics by win percentages.
    /// </summary>
    public class AvgScoreDeltaStats
    {
        public decimal Team1WinPct { get; set; }
        public decimal Team2WinPct { get; set; }
        public double  AvgDelta    { get; set; }
        public double  StdDevP     { get; set; }
        public int     SampleSize  { get; set; }
    }
}
