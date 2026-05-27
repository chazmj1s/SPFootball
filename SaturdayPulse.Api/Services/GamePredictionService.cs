using SaturdayPulse.Interfaces;
using SaturdayPulse.Contracts;
using SaturdayPulse.Contracts.Requests;
using SaturdayPulse.Contracts.Responses;
using SaturdayPulse.Models;

namespace SaturdayPulse.Services
{
    /// <summary>
    /// Predicts game scores based on team metrics and historical data.
    /// Uses IUnitOfWork for all data access — no direct EF/DbContext references.
    ///
    /// Single source of truth for predictions: WeeklyRankings.
    ///   • PredictMatchups loads the latest WeeklyRankings snapshot for the year.
    ///   • TeamRecords is NOT used here — it is reserved for Seed/Trend/Pedigree only.
    ///   • WeeklyRankings has the same fields as TeamRecords minus the rolling averages,
    ///     and reflects the current in-season state rather than the annual summary.
    /// </summary>
    public class GamePredictionService
    {
        private readonly IAvgScoreDifferentialService _avgScoreDifferentialService;
        private readonly IUnitOfWork                  _uow;
        private const    double                       HomeFieldAdvantage    = 2.5;
        private const    int                          RecentYearsForAverage = 5;
        private          double?                      _cachedAvgTeamScore;
        private          int                          _cachedAvgTeamScoreYear = -1;

        public GamePredictionService(
            IUnitOfWork uow,
            IAvgScoreDifferentialService avgScoreDifferentialService)
        {
            _uow                         = uow;
            _avgScoreDifferentialService = avgScoreDifferentialService;
        }

        // ── Public API ────────────────────────────────────────────────────────────

        /// <summary>
        /// Predicts the score for a single matchup using the latest WeeklyRankings snapshot.
        /// </summary>
        public async Task<GamePrediction> PredictMatchup(
            int year, string teamName, string opponentName,
            char location, int week = 0, CancellationToken token = default)
        {
            var team     = await _uow.Teams.GetByNameAsync(teamName, token)
                           ?? throw new ArgumentException($"Team not found: {teamName}");
            var opponent = await _uow.Teams.GetByNameAsync(opponentName, token)
                           ?? throw new ArgumentException($"Team not found: {opponentName}");

            var ratingsById = await GetLatestRatingsAsync(year, token);

            if (!ratingsById.TryGetValue(team.TeamId,     out var teamRating) ||
                !ratingsById.TryGetValue(opponent.TeamId, out var oppRating))
                throw new ArgumentException("WeeklyRankings not found for specified year.");

            var rivalries    = await _uow.Lookups.GetMatchupHistoriesAsync(token);
            var avgTeamScore = await GetAverageTeamScoreAsync(year, token);

            return CalculatePrediction(
                teamRating, oppRating, team, opponent, location,
                rivalries, avgTeamScore, year, week);
        }

        /// <summary>
        /// Predicts scores for multiple matchups in a single DB round-trip.
        /// Uses the latest WeeklyRankings snapshot for the year as the sole data source.
        /// </summary>
        public async Task<List<GamePrediction>> PredictMatchups(
            int year, List<MatchupRequest> matchups, CancellationToken token = default)
        {
            var teams        = await _uow.Teams.GetDictionaryByNameAsync(token);
            var ratingsById  = await GetLatestRatingsAsync(year, token);
            var rivalries    = await _uow.Lookups.GetMatchupHistoriesAsync(token);
            var avgTeamScore = await GetAverageTeamScoreAsync(year, token);

            var predictions = new List<GamePrediction>();

            foreach (var matchup in matchups)
            {
                if (!teams.TryGetValue(matchup.TeamName,     out var team)     ||
                    !teams.TryGetValue(matchup.OpponentName, out var opponent)) continue;

                if (!ratingsById.TryGetValue(team.TeamId,     out var teamRating) ||
                    !ratingsById.TryGetValue(opponent.TeamId, out var oppRating))  continue;

                predictions.Add(CalculatePrediction(
                    teamRating, oppRating, team, opponent, matchup.Location,
                    rivalries, avgTeamScore, year, matchup.Week));
            }

            return predictions.OrderByDescending(p => Math.Abs(p.ExpectedMargin)).ToList();
        }

        // ── Projection builder ────────────────────────────────────────────────────

        /// <summary>
        /// Maps a computed GamePrediction + game metadata to a Projection row
        /// ready for persistence. Called by WeeklyRankingsService step 17.
        /// </summary>
        public static Projection BuildProjection(
            GamePrediction prediction,
            int gameId, int year, int week,
            int homeTeamId, int awayTeamId)
        {
            var homeSpread = prediction.Location == 'H' || prediction.Location == 'W'
                ? prediction.ExpectedMargin
                : -prediction.ExpectedMargin;

            var total = prediction.PredictedTeamScore + prediction.PredictedOpponentScore;

            var homeWinProb = prediction.Location == 'H' || prediction.Location == 'W'
                ? prediction.WinProbability
                : prediction.OpponentWinProbability;

            return new Projection
            {
                GameId             = gameId,
                Year               = year,
                Week               = week,
                HomeTeamId         = homeTeamId,
                AwayTeamId         = awayTeamId,
                PredictedSpread    = (decimal)Math.Round(homeSpread,  1),
                PredictedTotal     = (decimal)Math.Round(total,       1),
                HomeWinProbability = (decimal)Math.Round(homeWinProb, 4)
            };
        }

        // ── Core prediction ───────────────────────────────────────────────────────

        private GamePrediction CalculatePrediction(
            WeeklyRanking teamRating, WeeklyRanking oppRating,
            Teams team, Teams opponent,
            char location,
            List<MatchupHistory> rivalries,
            double avgTeamScore,
            int year, int week)
        {
            var teamGamesPlayed = teamRating.Wins + teamRating.Losses;
            var oppGamesPlayed  = oppRating.Wins  + oppRating.Losses;

            // Use Ranking from WeeklyRankings — same scale the AvgScoreDifferential
            // table was built from, enabling the full ±3.0 differential range.
            var distribution = _avgScoreDifferentialService.GetExpectedDistribution(
                (double)(teamRating.Ranking ?? 0m),
                (double)(oppRating.Ranking  ?? 0m));

            var expectedFromTeam = distribution.ExpectedMargin;

            expectedFromTeam = RatingCalculator.ApplyHomeField(
                expectedFromTeam, location == 'H' || location == 'W', location == 'N', HomeFieldAdvantage);

            // PowerRating differential fine-tunes the spread beyond what win record alone predicts.
            if (teamRating.PowerRating.HasValue && oppRating.PowerRating.HasValue)
                expectedFromTeam += (double)(teamRating.PowerRating.Value - oppRating.PowerRating.Value) * 10.0;

            var normalizedT1 = Math.Min(team.TeamId, opponent.TeamId);
            var normalizedT2 = Math.Max(team.TeamId, opponent.TeamId);
            var rivalry      = rivalries.FirstOrDefault(
                r => r.Team1Id == normalizedT1 && r.Team2Id == normalizedT2);

            double  varianceMultiplier = RatingCalculator.RivalryVarianceMultiplierForDisplay(rivalry?.RivalryTier);
            string? rivalryNote        = rivalry != null
                ? $"{rivalry.RivalryName} ({rivalry.RivalryTier})" : null;

            // Score components from WeeklyRankings avg points data.
            var teamPPG = teamGamesPlayed > 0 ? teamRating.PointsFor     / (double)teamGamesPlayed : avgTeamScore;
            var oppPPG  = oppGamesPlayed  > 0 ? oppRating.PointsFor      / (double)oppGamesPlayed  : avgTeamScore;
            var teamPAG = teamGamesPlayed > 0 ? teamRating.PointsAgainst / (double)teamGamesPlayed : avgTeamScore;
            var oppPAG  = oppGamesPlayed  > 0 ? oppRating.PointsAgainst  / (double)oppGamesPlayed  : avgTeamScore;

            var predictedTeamScore = (teamPPG + oppPAG) / 2.0 + (expectedFromTeam / 2.0);
            var predictedOppScore  = (oppPPG  + teamPAG) / 2.0 - (expectedFromTeam / 2.0);

            double weekMultiplier = week switch { <= 4 => 1.05, >= 11 => 0.95, _ => 1.0 };
            predictedTeamScore   *= weekMultiplier;
            predictedOppScore    *= weekMultiplier;

            double scoringAdjustment = RatingCalculator.RivalryScoringAdjustment(rivalry?.RivalryTier);
            if (teamRating.Ranking.HasValue && teamRating.Ranking <= 25 &&
                oppRating.Ranking.HasValue  && oppRating.Ranking  <= 25)
                scoringAdjustment *= 0.95;
            if (week >= 15) scoringAdjustment *= 0.93;

            predictedTeamScore = Math.Max(0, predictedTeamScore * scoringAdjustment);
            predictedOppScore  = Math.Max(0, predictedOppScore  * scoringAdjustment);

            var stdDev        = distribution.StdDev * varianceMultiplier;
            var marginOfError = Math.Min(Math.Max(stdDev, AvgScoreDelta.DefaultAverageScoreDelta), 21.0);

            return new GamePrediction
            {
                GameId                 = 0,
                Week                   = week,
                TeamName               = team.TeamName,
                OpponentName           = opponent.TeamName,
                Location               = location,
                TeamWins               = (int)teamRating.Wins,
                OpponentWins           = (int)oppRating.Wins,
                PredictedTeamScore     = Math.Round(predictedTeamScore, 1),
                PredictedOpponentScore = Math.Round(predictedOppScore,  1),
                ExpectedMargin         = Math.Round(expectedFromTeam,   1),
                MarginOfError          = Math.Round(marginOfError,      1),
                RawStdDev              = stdDev,
                Confidence             = CalculateConfidence(stdDev, varianceMultiplier),
                RivalryNote            = rivalryNote,
                TeamPowerRating        = teamRating.PowerRating,
                OpponentPowerRating    = oppRating.PowerRating
            };
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the latest WeeklyRankings snapshot for the given year,
        /// keyed by TeamID. Falls back to the prior year if no snapshot exists
        /// for the requested year (handles new season bootstrap).
        /// </summary>
        private async Task<Dictionary<int, WeeklyRanking>> GetLatestRatingsAsync(
            int year, CancellationToken token)
        {
            var snapshots = await _uow.WeeklyRankings.GetDistinctYearWeeksAsync(token);

            // Try current year first, fall back to prior year if nothing exists yet.
            var lastSnapshot = snapshots
                .Where(s => s.Year == year)
                .OrderByDescending(s => s.Week)
                .FirstOrDefault();

            if (lastSnapshot == default)
            {
                lastSnapshot = snapshots
                    .Where(s => s.Year == year - 1)
                    .OrderByDescending(s => s.Week)
                    .FirstOrDefault();
            }

            if (lastSnapshot == default)
                return new Dictionary<int, WeeklyRanking>();

            var rankings = await _uow.WeeklyRankings
                .GetByYearAndWeekAsync(lastSnapshot.Year, lastSnapshot.Week, token);

            return rankings.ToDictionary(wr => wr.TeamID);
        }

        private async Task<double> GetAverageTeamScoreAsync(int year, CancellationToken token)
        {
            if (_cachedAvgTeamScore.HasValue && _cachedAvgTeamScoreYear == year)
                return _cachedAvgTeamScore.Value;

            var cutoffYear = year - RecentYearsForAverage;
            var games      = await _uow.Games.GetPlayedGamesSinceYearAsync(cutoffYear, token);

            _cachedAvgTeamScore = games.Count == 0
                ? 28.0
                : (games.Average(g => g.HomePoints) + games.Average(g => g.AwayPoints)) / 2.0;

            _cachedAvgTeamScoreYear = year;
            return _cachedAvgTeamScore.Value;
        }

        private static string CalculateConfidence(double stdDev, double varianceMultiplier)
        {
            var adjusted = stdDev * varianceMultiplier;
            if (adjusted < 10) return "High";
            if (adjusted < 14) return "Medium";
            if (adjusted < 18) return "Low";
            return "Very Low";
        }
    }
}
