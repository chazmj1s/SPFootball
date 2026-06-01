using SaturdayPulse.Interfaces;
using SaturdayPulse.Contracts;
using SaturdayPulse.Contracts.Requests;
using SaturdayPulse.Contracts.Responses;
using SaturdayPulse.Models;
using SaturdayPulse.Configuration;
using Microsoft.Extensions.Options;

namespace SaturdayPulse.Services
{
    /// <summary>
    /// Predicts game scores based on team metrics and historical data.
    /// Uses IUnitOfWork for all data access — no direct EF/DbContext references.
    ///
    /// Snapshot week logic:
    ///   Weeks 1-5  : use week 0 preseason snapshot for all predictions.
    ///                Prevents single early-season blowouts over weak opponents
    ///                from corrupting ratings before enough data accumulates.
    ///   Week 6+    : use week n-1 snapshot (current season data takes over).
    ///   Consistent across all years including historical backfill — week 0
    ///   was seeded from the prior year's final ratings in all cases.
    ///
    ///   SosWeekThreshold from MetricsConfiguration drives the cutoff (default 6).
    /// </summary>
    public class GamePredictionService
    {
        private readonly IAvgScoreDifferentialService _avgScoreDifferentialService;
        private readonly IUnitOfWork                  _uow;
        private readonly MetricsConfiguration         _config;
        private const    double                       HomeFieldAdvantage    = 2.5;
        private const    int                          RecentYearsForAverage = 5;
        private          double?                      _cachedAvgTeamScore;
        private          int                          _cachedAvgTeamScoreYear = -1;

        public GamePredictionService(
            IUnitOfWork uow,
            IAvgScoreDifferentialService avgScoreDifferentialService,
            IOptions<MetricsConfiguration> config)
        {
            _uow                         = uow;
            _avgScoreDifferentialService = avgScoreDifferentialService;
            _config                      = config.Value;
        }

        // ── Public API ────────────────────────────────────────────────────────────

        /// <summary>Predicts the score for a single matchup.</summary>
        public async Task<GamePrediction> PredictMatchup(
            int year, string teamName, string opponentName,
            char location, int week = 0, CancellationToken token = default)
        {
            var team     = await _uow.Teams.GetByNameAsync(teamName, token)
                           ?? throw new ArgumentException($"Team not found: {teamName}");
            var opponent = await _uow.Teams.GetByNameAsync(opponentName, token)
                           ?? throw new ArgumentException($"Team not found: {opponentName}");

            var recordsById    = await GetRatingsForWeekAsync(year, week, token);
            var rivalries      = await _uow.Lookups.GetMatchupHistoriesAsync(token);
            var avgTeamScore   = await GetAverageTeamScoreAsync(year, token);

            if (!recordsById.TryGetValue(team.TeamId,     out var teamRecord) ||
                !recordsById.TryGetValue(opponent.TeamId, out var oppRecord))
                throw new ArgumentException("Team records not found for specified year.");

            return CalculatePrediction(
                teamRecord, oppRecord, team, opponent, location,
                rivalries, avgTeamScore, year, week);
        }

        /// <summary>
        /// Sandbox: predicts a matchup between two teams from potentially different years.
        /// Each team's ratings are loaded from their respective year's end-of-season snapshot.
        /// Always neutral site (location = 'N'), week = 0.
        /// </summary>
        public async Task<GamePrediction> PredictSandboxMatchupAsync(
            string teamName, int teamYear,
            string opponentName, int opponentYear,
            CancellationToken token = default)
        {
            var team     = await _uow.Teams.GetByNameAsync(teamName,     token)
                           ?? throw new ArgumentException($"Team not found: {teamName}");
            var opponent = await _uow.Teams.GetByNameAsync(opponentName, token)
                           ?? throw new ArgumentException($"Team not found: {opponentName}");

            // Load each team's end-of-season ratings from their respective year
            var teamRecords = await GetEndOfSeasonRatingsAsync(teamYear,     token);
            var oppRecords  = await GetEndOfSeasonRatingsAsync(opponentYear, token);

            if (!teamRecords.TryGetValue(team.TeamId,     out var teamRecord))
                throw new ArgumentException($"No ratings found for {teamName} in {teamYear}.");
            if (!oppRecords.TryGetValue(opponent.TeamId,  out var oppRecord))
                throw new ArgumentException($"No ratings found for {opponentName} in {opponentYear}.");

            var rivalries    = await _uow.Lookups.GetMatchupHistoriesAsync(token);
            // Average team score across both years for realistic scoring context
            var avgTeamScore = await GetAverageTeamScoreAsync(Math.Min(teamYear, opponentYear), token);

            return CalculatePrediction(
                teamRecord, oppRecord, team, opponent, 'N',
                rivalries, avgTeamScore,
                Math.Max(teamYear, opponentYear), 0);
        }

        /// <summary>Predicts scores for multiple matchups in a single DB round-trip.</summary>
        public async Task<List<GamePrediction>> PredictMatchups(
            int year, List<MatchupRequest> matchups, CancellationToken token = default)
        {
            // Use the week from the first matchup — all matchups in a batch
            // are assumed to be from the same week.
            var week = matchups.FirstOrDefault()?.Week ?? 0;

            var teams        = await _uow.Teams.GetDictionaryByNameAsync(token);
            var recordsById  = await GetRatingsForWeekAsync(year, week, token);
            var rivalries    = await _uow.Lookups.GetMatchupHistoriesAsync(token);
            var avgTeamScore = await GetAverageTeamScoreAsync(year, token);

            var predictions = new List<GamePrediction>();

            foreach (var matchup in matchups)
            {
                if (!teams.TryGetValue(matchup.TeamName,     out var team)      ||
                    !teams.TryGetValue(matchup.OpponentName, out var opponent))  continue;

                if (!recordsById.TryGetValue(team.TeamId,     out var teamRecord) ||
                    !recordsById.TryGetValue(opponent.TeamId, out var oppRecord))  continue;

                predictions.Add(CalculatePrediction(
                    teamRecord, oppRecord, team, opponent, matchup.Location,
                    rivalries, avgTeamScore, year, matchup.Week));
            }

            return predictions.OrderByDescending(p => Math.Abs(p.ExpectedMargin)).ToList();
        }

        // ── Projection builder ────────────────────────────────────────────────────

        public static Projection BuildProjection(
            GamePrediction prediction,
            int gameId, int year, int week,
            int homeTeamId, int awayTeamId)
        {
            var homeSpread = prediction.Location == 'H'
                ? prediction.ExpectedMargin
                : -prediction.ExpectedMargin;

            var total = prediction.PredictedTeamScore + prediction.PredictedOpponentScore;

            var homeWinProb = prediction.Location == 'H'
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

        // ── Ratings loader ────────────────────────────────────────────────────────

        /// <summary>
        /// Returns end-of-season ratings for a team's year — uses the highest
        /// available WeeklyRankings snapshot for that year, giving full season strength.
        /// Falls back to week 0 (preseason) if no in-season snapshots exist.
        /// </summary>
        private async Task<Dictionary<int, TeamRecord>> GetEndOfSeasonRatingsAsync(
            int year, CancellationToken token)
        {
            var yearWeeks = await _uow.WeeklyRankings.GetDistinctYearWeeksAsync(token);
            var maxWeek   = yearWeeks
                .Where(yw => yw.Year == year)
                .Select(yw => yw.Week)
                .DefaultIfEmpty(0)
                .Max();

            return await GetRatingsForWeekAsync(year, maxWeek, token);
        }

        // ── Ratings loader (original) ─────────────────────────────────────────────

        /// <summary>
        /// Returns the team ratings dictionary to use for prediction at a given week.
        ///
        /// Weeks 1-5  → week 0 preseason snapshot (stable, prevents early noise)
        /// Week 6+    → week n-1 snapshot (current season data)
        /// Week 0     → week 0 snapshot (preseason — used during initialization)
        ///
        /// Falls back to prior year final snapshot if week 0 doesn't exist.
        /// Falls back to TeamRecords if no WeeklyRankings snapshot is available.
        /// </summary>
        private async Task<Dictionary<int, TeamRecord>> GetRatingsForWeekAsync(
            int year, int week, CancellationToken token)
        {
            // Determine which snapshot week to use.
            int snapshotWeek = week < _config.SosWeekThreshold
                ? 0          // weeks 0-5: use preseason baseline
                : week - 1;  // week 6+: use prior week

            // Try current year snapshot first.
            var snapshot = await _uow.WeeklyRankings
                .GetByYearAndWeekAsync(year, snapshotWeek, token);

            // Fall back to prior year final snapshot if current year week 0
            // doesn't exist yet (season not initialized).
            if (!snapshot.Any())
            {
                var priorSnapshots = await _uow.WeeklyRankings
                    .GetDistinctYearWeeksAsync(token);

                var priorSnapshot = priorSnapshots
                    .Where(s => s.Year == year - 1)
                    .OrderByDescending(s => s.Week)
                    .FirstOrDefault();

                if (priorSnapshot != default)
                    snapshot = await _uow.WeeklyRankings
                        .GetByYearAndWeekAsync(priorSnapshot.Year, priorSnapshot.Week, token);
            }

            // If we have a snapshot, build synthetic TeamRecords from it.
            var result = snapshot.Any()
                ? snapshot.ToDictionary(
                    wr => wr.TeamID,
                    wr => new TeamRecord
                    {
                        TeamID        = wr.TeamID,
                        Year          = wr.Year,
                        Wins          = wr.Wins,
                        Losses        = wr.Losses,
                        PointsFor     = wr.PointsFor,
                        PointsAgainst = wr.PointsAgainst,
                        PowerRating   = wr.PowerRating,
                        Ranking       = wr.Ranking,
                        CombinedSOS   = wr.CombinedSOS,
                        BaseSOS       = wr.BaseSOS,
                        SubSOS        = wr.SubSOS
                    })
                : (await _uow.TeamRecords.GetByYearAsync(year, token))
                    .ToDictionary(tr => tr.TeamID);

            // Add FCS placeholder records for teams not in the snapshot.
            // FCS teams don't have WeeklyRankings entries — without a placeholder
            // they default to Ranking=0 (average) producing wildly wrong predictions.
            // Ranking 0.03 / PowerRating -0.50 represents a typical FCS program
            // and produces ~28-30 point expected margin vs an average FBS opponent,
            // consistent with actual FBS vs FCS game results.
            var allTeams = await _uow.Teams.GetAllAsync(token);
            foreach (var team in allTeams.Where(t =>
                string.Equals(t.Division, "fcs", StringComparison.OrdinalIgnoreCase) &&
                !result.ContainsKey(t.TeamId)))
            {
                result[team.TeamId] = new TeamRecord
                {
                    TeamID      = team.TeamId,
                    Year        = (short)year,
                    Ranking     = 0.03m,
                    PowerRating = -0.50m,
                    Wins        = 0,
                    Losses      = 0,
                    PointsFor   = 280,   // ~20 pts/game — typical FCS scoring
                    PointsAgainst = 420  // ~30 pts/game allowed — typical FCS defense
                };
            }

            return result;
        }

        // ── Core prediction ───────────────────────────────────────────────────────

        private GamePrediction CalculatePrediction(
            TeamRecord teamRecord, TeamRecord oppRecord,
            Teams team, Teams opponent,
            char location,
            List<MatchupHistory> rivalries,
            double avgTeamScore,
            int year, int week)
        {
            var teamGamesPlayed = teamRecord.Wins + teamRecord.Losses;
            var oppGamesPlayed  = oppRecord.Wins  + oppRecord.Losses;

            // Use Ranking values for the differential lookup — matches the scale
            // used when building the AvgScoreDifferential table (ExpandStrength(Ranking)).
            // BucketWinPct (0-1 range) produces differentials far too small for the table.
            var distribution = _avgScoreDifferentialService.GetExpectedDistribution(
                (double)(teamRecord.Ranking ?? 0m),
                (double)(oppRecord.Ranking  ?? 0m));

            var expectedFromTeam = distribution.ExpectedMargin;
            expectedFromTeam     = RatingCalculator.ApplyHomeField(
                expectedFromTeam, location == 'H', location == 'N', HomeFieldAdvantage);

            if (teamRecord.PowerRating.HasValue && oppRecord.PowerRating.HasValue)
                expectedFromTeam += (double)(teamRecord.PowerRating.Value - oppRecord.PowerRating.Value) * 10.0;

            var normalizedT1 = Math.Min(team.TeamId, opponent.TeamId);
            var normalizedT2 = Math.Max(team.TeamId, opponent.TeamId);
            var rivalry      = rivalries.FirstOrDefault(
                r => r.Team1Id == normalizedT1 && r.Team2Id == normalizedT2);

            double  varianceMultiplier = RatingCalculator.RivalryVarianceMultiplierForDisplay(rivalry?.RivalryTier);
            string? rivalryNote        = rivalry != null
                ? $"{rivalry.RivalryName} ({rivalry.RivalryTier})" : null;

            var teamPPG = teamGamesPlayed > 0 ? teamRecord.PointsFor     / (double)teamGamesPlayed : 28.0;
            var oppPPG  = oppGamesPlayed  > 0 ? oppRecord.PointsFor      / (double)oppGamesPlayed  : 28.0;
            var teamPAG = teamGamesPlayed > 0 ? teamRecord.PointsAgainst / (double)teamGamesPlayed : 28.0;
            var oppPAG  = oppGamesPlayed  > 0 ? oppRecord.PointsAgainst  / (double)oppGamesPlayed  : 28.0;

            var predictedTeamScore = (teamPPG + oppPAG) / 2.0 + (expectedFromTeam / 2.0);
            var predictedOppScore  = (oppPPG  + teamPAG) / 2.0 - (expectedFromTeam / 2.0);

            double weekMultiplier = week switch { <= 4 => 1.05, >= 11 => 0.95, _ => 1.0 };
            predictedTeamScore   *= weekMultiplier;
            predictedOppScore    *= weekMultiplier;

            double scoringAdjustment = RatingCalculator.RivalryScoringAdjustment(rivalry?.RivalryTier);
            if (teamRecord.Ranking.HasValue && teamRecord.Ranking <= 25 &&
                oppRecord.Ranking.HasValue  && oppRecord.Ranking  <= 25)
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
                TeamWins               = (int)teamRecord.Wins,
                OpponentWins           = (int)oppRecord.Wins,
                PredictedTeamScore     = Math.Round(predictedTeamScore, 1),
                PredictedOpponentScore = Math.Round(predictedOppScore,  1),
                ExpectedMargin         = Math.Round(expectedFromTeam,   1),
                MarginOfError          = Math.Round(marginOfError,      1),
                RawStdDev              = stdDev,
                Confidence             = CalculateConfidence(stdDev, varianceMultiplier),
                RivalryNote            = rivalryNote,
                TeamPowerRating        = teamRecord.PowerRating,
                OpponentPowerRating    = oppRecord.PowerRating
            };
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

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
