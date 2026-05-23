using Microsoft.Extensions.Logging;
using SaturdayPulse.Contracts;
using SaturdayPulse.Models;
using SaturdayPulse.Data;

namespace SaturdayPulse.Utilities
{
    /// <summary>
    /// Calculates and populates matchup-specific historical statistics.
    /// Used to identify high-variance rivalries by comparing actual matchup performance
    /// to expected performance from win-based AvgScoreDeltas.
    /// </summary>
    public class MatchupHistoryCalculator(IUnitOfWork _uow, ILogger<MatchupHistoryCalculator> _logger)
    {
        private class GameData
        {
            public int? Team1    { get; set; }
            public int? Team2    { get; set; }
            public int? Margin   { get; set; }
            public int? Year     { get; set; }
            public int? WinnerId { get; set; }
            public int? LoserId  { get; set; }
        }

        /// <summary>
        /// Calculates matchup history for all 50 curated Epic, National, State, and MEH tier rivalries.
        /// All rivalries in the seed data have sufficient game history (50+ games).
        /// </summary>
        public async Task<int> CalculateAllMatchupHistories(CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Starting matchup history calculation for all rivalry tiers");

            var rivalryMetadata = RivalrySeedData.GetRivalries();
            _logger.LogInformation("Found {Count} rivalries to process", rivalryMetadata.Count);

            // Load all games, teams, and records into memory for efficient processing
            var allGames = (await _uow.Games.GetPlayedGamesSinceYearAsync(1, cancellationToken))
                .Select(g => new GameData
                {
                    Team1    = g.HomeId,
                    Team2    = g.AwayId,
                    Margin   = g.HomePoints - g.AwayPoints,
                    Year     = g.Year,
                    WinnerId = g.HomePoints > g.AwayPoints ? g.HomeId : g.AwayId,
                    LoserId  = g.HomePoints < g.AwayPoints ? g.HomeId : g.AwayId
                })
                .ToList();

            var teamMapping = await _uow.Teams.GetAllAsync(cancellationToken);

            // Pre-load win totals for upset rate calculation — eliminates N+1 query
            var allRecords = await _uow.TeamRecords.GetHistoricalAsync(1, 9999, cancellationToken);
            var winsLookup = allRecords
                .GroupBy(r => (r.TeamID, (int)r.Year))
                .ToDictionary(g => g.Key, g => (int)g.First().Wins);

            var matchupHistories = new List<MatchupHistory>();

            foreach (var rivalry in rivalryMetadata)
            {
                // Find team IDs (check both TeamName and Alias)
                var team1Id = teamMapping.FirstOrDefault(t =>
                    t.TeamName.Equals(rivalry.Team1Name, StringComparison.OrdinalIgnoreCase) ||
                    (t.Alias != null && t.Alias.Equals(rivalry.Team1Name, StringComparison.OrdinalIgnoreCase)))?.TeamId;

                var team2Id = teamMapping.FirstOrDefault(t =>
                    t.TeamName.Equals(rivalry.Team2Name, StringComparison.OrdinalIgnoreCase) ||
                    (t.Alias != null && t.Alias.Equals(rivalry.Team2Name, StringComparison.OrdinalIgnoreCase)))?.TeamId;

                if (team1Id == null || team2Id == null)
                {
                    _logger.LogWarning("Could not find team IDs for rivalry {RivalryName} ({Team1} vs {Team2})",
                        rivalry.RivalryName, rivalry.Team1Name, rivalry.Team2Name);
                    continue;
                }

                // Normalize so lower ID is always Team1
                var normalizedTeam1 = Math.Min(team1Id.Value, team2Id.Value);
                var normalizedTeam2 = Math.Max(team1Id.Value, team2Id.Value);

                var games = allGames
                    .Where(g => g.Team1 == normalizedTeam1 && g.Team2 == normalizedTeam2)
                    .ToList();

                if (games.Count == 0)
                {
                    _logger.LogWarning("No games found for rivalry {RivalryName} ({Team1} vs {Team2})",
                        rivalry.RivalryName, rivalry.Team1Name, rivalry.Team2Name);
                    continue;
                }

                var gameCount = games.Count;

                // Average margin and standard deviation
                var margins   = games.Select(g => Math.Abs((decimal)g.Margin)).ToList();
                var avgMargin = margins.Average();
                var variance  = margins.Sum(m => Math.Pow((double)(m - avgMargin), 2)) / gameCount;
                var stDev     = Math.Sqrt(variance);

                // Upset rate calculated in memory using pre-loaded wins lookup
                var upsets = CalculateUpsetRate(games, winsLookup);

                matchupHistories.Add(new MatchupHistory
                {
                    Team1Id     = normalizedTeam1,
                    Team2Id     = normalizedTeam2,
                    GamesPlayed = gameCount,
                    AvgMargin   = (decimal)avgMargin,
                    StDevMargin = (decimal)stDev,
                    UpsetRate   = (decimal)upsets,
                    FirstPlayed = (int)games.Min(g => g.Year),
                    LastPlayed  = (int)games.Max(g => g.Year),
                    RivalryName = rivalry.RivalryName,
                    RivalryTier = rivalry.Tier
                });
            }

            // Clear existing and insert new — same pattern as ClearAvgScoreDeltasAsync
            await _uow.Lookups.ClearMatchupHistoriesAsync(cancellationToken);
            await _uow.Lookups.AddMatchupHistoriesAsync(matchupHistories, cancellationToken);
            var saved = await _uow.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Saved {Count} matchup histories to database", matchupHistories.Count);
            return matchupHistories.Count;
        }

        /// <summary>
        /// Calculates upset rate in memory using the pre-loaded wins lookup.
        /// Upset = team with fewer season wins won the game.
        /// No per-game DB calls — eliminates the N+1 query from the original implementation.
        /// </summary>
        private static double CalculateUpsetRate(
            List<GameData> games,
            Dictionary<(int teamId, int year), int> winsLookup)
        {
            var upsetCount = 0;

            foreach (var game in games)
            {
                winsLookup.TryGetValue(((int teamId, int year))(game.WinnerId, game.Year), out var winnerWins);
                winsLookup.TryGetValue(((int teamId, int year))(game.LoserId, game.Year), out var loserWins);

                if (loserWins > winnerWins)
                    upsetCount++;
            }

            return games.Count > 0 ? (double)upsetCount / games.Count : 0.0;
        }
    }
}
