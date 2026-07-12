using SaturdayPulse.Interfaces;
using SaturdayPulse.Contracts;
using SaturdayPulse.Contracts.Requests;
using SaturdayPulse.Contracts.Responses;
using SaturdayPulse.Models;
using SaturdayPulse.Configuration;
using SaturdayPulse.Extensions;
using Microsoft.Extensions.Options;

namespace SaturdayPulse.Services
{
    /// <summary>
    /// Predicts game scores based on team metrics and historical data.
    /// Uses IUnitOfWork for all data access — no direct EF/DbContext references.
    ///
    /// ── PROMOTED — K=4 inertia blend is the live rating source ──────────────────
    ///   GetRatingsForWeekAsync delegates to ExperimentalInertiaRatingService.
    ///   GetBlendedRatingsForWeekAsync — a data-volume-weighted blend of the
    ///   TrendRating-derived preseason anchor and live in-season PowerRating, no
    ///   hard cliff at any week. Replaces the old week-6 snapshot-cliff logic
    ///   (weeks 1-5 frozen on the week-0 preseason snapshot, week 6+ switching to
    ///   week n-1), validated across a full 2025 season against the old logic and
    ///   Vegas (full 12-year historical TrendRating refresh, HFA=3.5,
    ///   compareRatingAccuracy results: 2.12 MAE / 4.1 winner-accuracy-point
    ///   improvement in weeks 1-5 — the exact window the old cliff broke — with no
    ///   regression in weeks 6+). Every public caller — PredictMatchup,
    ///   PredictMatchups, PredictSandboxMatchupAsync — inherits this automatically.
    ///
    ///   ZRoster is no longer applied per-game inside this class — ApplyZRosterDecay,
    ///   CloneWithAdjustedPowerRating, and ZRosterScalingConstant have been removed
    ///   (their only purpose was decaying a raw ZRoster value that the new blend
    ///   already folds into the anchor once, upstream, in RatingBlendingService.
    ///   ComputeSeededAnchorUnit). The Degraded() extension method they used may
    ///   also be dead now — check for other callers before removing that file.
    ///
    ///   GetEndOfSeasonRatingsAsync (used by PredictSandboxMatchupAsync) reads
    ///   TeamRecords directly rather than going through the blend — confirmed with
    ///   Charlie that the admin "compute weekly" task keeps TeamRecords in sync
    ///   with WeeklyRankings all season, so it already holds the true final value
    ///   with no dilution, which is what a historical what-if matchup needs rather
    ///   than a live week-to-week prediction input.
    ///
    ///   RatingComparisonService / ExperimentalInertiaRatingService are KEPT (not
    ///   deprecated) as reusable scaffolding for whatever gets compared against this
    ///   new baseline next — see GetProductionRatingsForComparisonAsync below, which
    ///   now returns this same K=4-blended output under its existing name/contract.
    ///
    ///   NOTE: WeeklyRankingsService has its OWN, separate SosWeekThreshold-gated
    ///   cliff (projected vs. actual wins for SOS, weeks 1-5 vs 6+) — untouched by
    ///   this change, opened as a separate action item, not in scope here.
    ///
    ///   Old cliff behavior is retired, not preserved inline — see this session's
    ///   history for the full old implementation if it's ever needed again.
    /// </summary>
    public class GamePredictionService
    {
        private readonly IAvgScoreDifferentialService _avgScoreDifferentialService;
        private readonly IUnitOfWork                  _uow;
        private readonly ExperimentalInertiaRatingService _blendedRating;
        private readonly MetricsConfiguration         _config;
        private const    int                          RecentYearsForAverage = 5;
        private          double?                      _cachedAvgTeamScore;
        private          int                          _cachedAvgTeamScoreYear = -1;

        public GamePredictionService(
            IUnitOfWork uow,
            IAvgScoreDifferentialService avgScoreDifferentialService,
            ExperimentalInertiaRatingService blendedRating,
            IOptions<MetricsConfiguration> config)
        {
            _uow                         = uow;
            _avgScoreDifferentialService = avgScoreDifferentialService;
            _blendedRating               = blendedRating;
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
                rivalries, avgTeamScore, year, week, null);
        }

        /// <summary>
        /// Sandbox: predicts a matchup between two teams from potentially different years.
        /// Each team's ratings are loaded from their respective year's true final
        /// TeamRecords values (see GetEndOfSeasonRatingsAsync) — not the K=4 blend
        /// used for live week-to-week predictions elsewhere in this class.
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
                Math.Max(teamYear, opponentYear), 0, null);
        }

        /// <summary>
        /// Predicts scores for multiple matchups in a single DB round-trip, as of a
        /// given week's data.
        ///
        /// `asOfWeek` is the week this projection is being made FROM — e.g. "using
        /// everything we know as of week 0" — and applies to every matchup in the
        /// batch, since they're all rated off the same K=4-blended snapshot of team
        /// strength for that week.
        /// </summary>
        public async Task<List<GamePrediction>> PredictMatchups(
            int year, int asOfWeek, List<MatchupRequest> matchups, CancellationToken token = default)
        {
            var teams        = await _uow.Teams.GetDictionaryByNameAsync(token);
            var recordsById  = await GetRatingsForWeekAsync(year, asOfWeek, token);
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
                    rivalries, avgTeamScore, year, matchup.Week, null));
            }

            return predictions.OrderByDescending(p => Math.Abs(p.ExpectedMargin)).ToList();
        }

        // ── Comparison-path entry points — KEPT for future experiments ──────────────

        /// <summary>
        /// Read-only wrapper exposing the current rating lookup for comparison against
        /// future alternatives. As of this promotion, this returns the K=4-blended
        /// output (same as every production caller) — the "production" side of any
        /// future RatingComparisonService run is now this method's output, same
        /// contract/name as when it wrapped the old snapshot-cliff logic.
        /// </summary>
        public Task<Dictionary<int, TeamRecord>> GetProductionRatingsForComparisonAsync(
            int year, int week, CancellationToken token = default)
            => GetRatingsForWeekAsync(year, week, token);

        /// <summary>
        /// Same prediction math as PredictMatchups, but takes a pre-built ratings
        /// dictionary instead of resolving one internally via GetRatingsForWeekAsync.
        /// Lets external comparison tooling (RatingComparisonService) run the identical
        /// CalculatePrediction logic against an alternate ratings source — e.g. a
        /// future experimental candidate — without duplicating this logic.
        /// </summary>
        public async Task<List<GamePrediction>> PredictMatchupsWithRatings(
            int year, Dictionary<int, TeamRecord> recordsById, List<MatchupRequest> matchups,
            double? hfaOverride, CancellationToken token = default)
        {
            var teams        = await _uow.Teams.GetDictionaryByNameAsync(token);
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
                    rivalries, avgTeamScore, year, matchup.Week, hfaOverride));
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
        /// Returns end-of-season ratings for a team's year — reads TeamRecords
        /// directly rather than routing through GetRatingsForWeekAsync/the K=4 blend.
        ///
        /// Confirmed with Charlie: the admin "compute weekly" task
        /// (DeveloperService.ComputeWeeklyAsync → WeeklyRankingsService.
        /// ComputeAndSaveAsync) keeps TeamRecords in sync with the latest
        /// WeeklyRankings data every week it's run, all season — so for a
        /// completed past year, TeamRecords already holds the true final value,
        /// with no dilution. Bypassing the blend here (rather than accepting the
        /// ~20-25% residual anchor weight the K=4 formula always carries, even at
        /// a full season's games played) is what PredictSandboxMatchupAsync
        /// actually needs — a clean historical "what was this team's real final
        /// rating" lookup, not a live week-to-week prediction input.
        ///
        /// FCS placeholder preserved here to match GetBlendedRatingsForWeekAsync's
        /// handling — FCS teams don't go through the normal weekly computation
        /// pipeline, so their TeamRecords.PowerRating is typically unset. Without
        /// this, a sandbox matchup involving an FCS team would silently fail or
        /// return a nonsense (null/zero) rating instead of a sensible placeholder.
        /// </summary>
        private async Task<Dictionary<int, TeamRecord>> GetEndOfSeasonRatingsAsync(
            int year, CancellationToken token)
        {
            var teamRecords = await _uow.TeamRecords.GetByYearAsync(year, token);
            var result = teamRecords.ToDictionary(tr => tr.TeamID);

            var allTeams = await _uow.Teams.GetAllAsync(token);
            foreach (var team in allTeams.Where(t =>
                string.Equals(t.Division, "fcs", StringComparison.OrdinalIgnoreCase) &&
                !result.ContainsKey(t.TeamId)))
            {
                result[team.TeamId] = new TeamRecord
                {
                    TeamID           = team.TeamId,
                    Year             = (short)year,
                    Ranking          = 0.03m,
                    PowerRating      = -0.50m,
                    Wins             = 0,
                    Losses           = 0,
                    PointsFor        = 280,
                    PointsAgainst    = 420,
                    AvgPointsScored  = 20m,
                    AvgPointsAllowed = 30m
                };
            }

            return result;
        }

        // ── Ratings loader (PROMOTED — K=4 inertia blend) ───────────────────────────

        /// <summary>
        /// Returns the team ratings dictionary to use for prediction at a given week.
        ///
        /// PROMOTED: delegates to ExperimentalInertiaRatingService.
        /// GetBlendedRatingsForWeekAsync — data-volume-weighted blend of the
        /// TrendRating-derived preseason anchor and live in-season PowerRating, no
        /// hard cliff at any week. Replaces the old week-6 snapshot-cliff logic
        /// (weeks 1-5 frozen on week 0, week 6+ switching to week n-1), validated
        /// via a full-season accuracy comparison against that old logic and Vegas —
        /// see class remarks for the specific numbers.
        ///
        /// FCS placeholder handling, AvgPointsScored/AvgPointsAllowed mapping, and
        /// ZRoster folding are all handled inside GetBlendedRatingsForWeekAsync
        /// itself now — nothing left to do in this wrapper.
        /// </summary>
        private Task<Dictionary<int, TeamRecord>> GetRatingsForWeekAsync(
            int year, int week, CancellationToken token)
            => _blendedRating.GetBlendedRatingsForWeekAsync(year, week, token);

        // ── Core prediction ───────────────────────────────────────────────────────

        private GamePrediction CalculatePrediction(
            TeamRecord teamRecord, TeamRecord oppRecord,
            Teams team, Teams opponent,
            char location,
            List<MatchupHistory> rivalries,
            double avgTeamScore,
            int year, int week,
            double? hfaOverride)
        {
            // Use Ranking values for the differential lookup — matches the scale
            // used when building the AvgScoreDifferential table (ExpandStrength(Ranking)).
            // BucketWinPct (0-1 range) produces differentials far too small for the table.
            var distribution = _avgScoreDifferentialService.GetExpectedDistribution(
                (double)(teamRecord.Ranking ?? 0m),
                (double)(oppRecord.Ranking  ?? 0m));

            var expectedFromTeam = distribution.ExpectedMargin;
            expectedFromTeam     = RatingCalculator.ApplyHomeField(
                expectedFromTeam, location == 'H', location == 'N', (double)(hfaOverride.HasValue ? hfaOverride : _config.HomeFieldAdvantage));

            if (teamRecord.PowerRating.HasValue && oppRecord.PowerRating.HasValue)
                expectedFromTeam += (double)(teamRecord.PowerRating.Value - oppRecord.PowerRating.Value) * 10.0;

            var normalizedT1 = Math.Min(team.TeamId, opponent.TeamId);
            var normalizedT2 = Math.Max(team.TeamId, opponent.TeamId);
            var rivalry      = rivalries.FirstOrDefault(
                r => r.Team1Id == normalizedT1 && r.Team2Id == normalizedT2);

            double  varianceMultiplier = RatingCalculator.RivalryVarianceMultiplierForDisplay(rivalry?.RivalryTier);
            string? rivalryNote        = rivalry != null
                ? $"{rivalry.RivalryName} ({rivalry.RivalryTier})" : null;

            var teamPPG = (double)teamRecord.AvgPointsScored;
            var oppPPG  = (double)oppRecord.AvgPointsScored;
            var teamPAG = (double)teamRecord.AvgPointsAllowed;
            var oppPAG  = (double)oppRecord.AvgPointsAllowed;

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
