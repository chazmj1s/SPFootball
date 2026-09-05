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
    ///
    /// ── REBUILT — margin/total/confidence data-correctness pass ─────────────────
    ///   Root issue found: PowerRating is itself derived from Ranking + SOS + Record
    ///   (confirmed with Charlie). The margin calc was keying the historical bucket
    ///   lookup on Ranking AND THEN applying a ×10 PowerRating-delta correction on
    ///   top — double- (for SOS/Record, triple-) counting the same underlying
    ///   strength signal. Verified against live Week 20 2025 WeeklyRankings data:
    ///   Texas Ranking 0.8314 / PowerRating 0.0808, Alabama Ranking 0.8593 /
    ///   PowerRating 0.1718 — two closely-related, not independent, numbers. This
    ///   was very likely why closely-rated teams (e.g. Texas/Alabama, Sandbox) were
    ///   compressing toward a near-zero margin regardless of what the historical
    ///   bucket for their differential actually said. The ×10 correction has been
    ///   removed; AvgScoreDifferentialService's interpolated AverageMargin (keyed
    ///   purely on Ranking) is now the sole margin source.
    ///
    ///   Total points previously came ENTIRELY from team PPG/PAG averaging —
    ///   AvgScoreDifferential.AverageTotalPoints existed in the DB but was never
    ///   mapped through to this class at all. Total points is now a reliability-
    ///   weighted blend: the bucket's own historical AverageTotalPoints anchors the
    ///   number, corroborated (not overridden) by these two teams' actual PPG/PAG,
    ///   weighted by the bucket's own ReliabilityWeight — well-sampled buckets lean
    ///   on 60 years of history, thin buckets lean more on this year's real scoring.
    ///
    ///   MarginOfError/RawStdDev no longer floor/cap against constants borrowed from
    ///   the retired AvgScoreDelta class ([7, 21]) — they report the real,
    ///   interpolated historical StdDevMargin for this differential directly.
    ///
    ///   Confidence is rebuilt on two data-derived signals instead of fixed point
    ///   thresholds (previously <10/<14/<18 on raw stddev, calibrated against a
    ///   bucket system that's no longer in use): a baseline volatility tier from
    ///   this matchup's stddev percentile within the FULL AvgScoreDifferential
    ///   table's own distribution (self-calibrating — moves with the data, not a
    ///   guessed cutoff), modulated by a game-specific corroboration check — do
    ///   these two teams' OffensiveZScore/DefensiveZScore edges point the same
    ///   direction as the historical margin. SOS/PowerRating/Record/Win% are
    ///   deliberately excluded from corroboration since they're already folded into
    ///   the Ranking-keyed margin itself (same double-counting problem as above);
    ///   OffensiveZScore/DefensiveZScore are the only genuinely independent signals
    ///   left. The corroboration adjustment is itself gated by the bucket's own
    ///   ReliabilityWeight — a thin bucket doesn't let a single game's Z-score
    ///   agreement override the baseline in either direction.
    ///
    /// ── FOLLOW-UP — week=0 / ranked-check / postseason-check cleanup ────────────
    ///   PredictSandboxMatchupAsync always calls CalculatePrediction with week = 0.
    ///   That was silently triggering the "early season" branch of weekMultiplier
    ///   (a real calendar-scoring-variance adjustment with no meaning for a
    ///   hypothetical, possibly cross-year, neutral-site matchup) on every single
    ///   Sandbox prediction. Now gated behind an explicit
    ///   applyWeeklyScoringAdjustments flag — true for all real calendar-anchored
    ///   callers (PredictMatchup/PredictMatchups/PredictMatchupsWithRatings,
    ///   unchanged behavior), false for PredictSandboxMatchupAsync.
    ///
    ///   Also removed from scoringAdjustment: a "ranked vs ranked" check that
    ///   compared TeamRecord.Ranking (continuous ~0-1 Rating) against <= 25 — never
    ///   true criteria, since Ranking is never > 25; it silently fired on every
    ///   matchup with any Ranking at all rather than genuinely detecting top-25
    ///   status. No ordinal rank field exists on TeamRecord to do this correctly
    ///   today, so it was removed rather than patched with another guess.
    ///
    ///   Also removed: a week >= 15 postseason-scoring-compression multiplier — an
    ///   unreliable proxy for conference championship week, which doesn't land on a
    ///   fixed week number every season.
    ///
    ///   These three combined were coincidentally near-canceling for the Texas/
    ///   Alabama Sandbox case (1.05 weekMultiplier × 0.95 always-on "ranked" hack ≈
    ///   0.9975), which is why the total looked roughly reasonable despite every
    ///   input being wrong. Not something to rely on — worth re-validating total
    ///   points on a few more Sandbox pairings now that both bugs are gone at once.
    ///
    ///   RivalryScoringAdjustment/RivalryVarianceMultiplier/
    ///   RivalryVarianceMultiplierForDisplay (in RatingCalculator) still use
    ///   hand-picked EPIC/NATIONAL/STATE tier constants — flagged as the next
    ///   candidate for a MatchupHistory-driven replacement (real per-pair AvgMargin/
    ///   StdDevMargin/VarianceRatio instead of a tier lookup), not addressed here:
    ///   RatingCalculator is shared with other services and MatchupHistory.
    ///   VarianceRatio's current baseline needs verifying before it's trusted for
    ///   this.
    ///
    /// ── FOLLOW-UP RESOLVED — rivalry adjustments now data-driven (display side) ──
    ///   RivalryVarianceMultiplierForDisplay and RivalryScoringAdjustment now take
    ///   the real MatchupHistory row (Layer 1: AvgTotalPoints column added; Layer 2:
    ///   MatchupHistoryCalculator backfilled from actual game data for the 50
    ///   curated rivalries) and the live, interpolated AvgScoreDifferential values
    ///   for the pair's current differential, computing a real ratio instead of an
    ///   EPIC/NATIONAL/STATE guess. See RatingCalculator.cs remarks for the full
    ///   detail — notably, RivalryVarianceMultiplierForDisplay reduces cleanly to
    ///   "use this pair's own real historical StdDevMargin directly" for known
    ///   rivalries. VarianceRatio itself was confirmed (by reading
    ///   MatchupHistoryCalculator directly) to have never been populated at all —
    ///   not stale, just never wired up — so this computes the ratio live at
    ///   prediction time instead of trusting that unset field.
    ///
    ///   RivalryVarianceMultiplier (no "ForDisplay" suffix) is confirmed via
    ///   reference search to also feed ComputeGameZScore, TeamMetricsService, and
    ///   WeeklyRankingsService — the live weekly rating pipeline, not just
    ///   prediction display — and remains untouched, deferred to the planned
    ///   calc-engine refactor where its wider blast radius can be validated properly.
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
        private          List<AvgScoreDifferential>?   _cachedDifferentials;

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

            var recordsById     = await GetRatingsForWeekAsync(year, week, token);
            var rivalries       = await _uow.Lookups.GetMatchupHistoriesAsync(token);
            var avgTeamScore    = await GetAverageTeamScoreAsync(year, token);
            var allDifferentials = await GetAllDifferentialsAsync(token);

            if (!recordsById.TryGetValue(team.TeamId,     out var teamRecord) ||
                !recordsById.TryGetValue(opponent.TeamId, out var oppRecord))
                throw new ArgumentException("Team records not found for specified year.");

            return CalculatePrediction(
                teamRecord, oppRecord, team, opponent, location,
                rivalries, avgTeamScore, allDifferentials, year, week, null);
        }

        /// <summary>Full margin-calculation breakdown for AnalyzePredictionMathAsync's diagnostic output.</summary>
        public record PredictionMathDetail(
            ExperimentalInertiaRatingService.TeamAnchorDetail Team,
            ExperimentalInertiaRatingService.TeamAnchorDetail Opponent,
            double StrengthDifferential, double ExpectedMarginBeforeHomeField,
            double ExpectedMarginAfterHomeField, double StdDev, int BucketSampleSize,
            double BucketReliability);

        /// <summary>One game's PredictionMathDetail plus the schedule context AnalyzeSeasonPredictionMathAsync loops over.</summary>
        public record SeasonPredictionMathGame(
            int Week, int OpponentId, string OpponentName, char Location,
            PredictionMathDetail Detail);

        /// <summary>
        /// DIAGNOSTIC — READ-ONLY. Shares all remarks with AnalyzePredictionMathAsync
        /// below — same math, same real methods, no second implementation. Given
        /// only a team and a year, loops that team's actual schedule
        /// (_uow.Games.GetByYearAsync) and runs the same per-game analysis using
        /// EACH GAME'S OWN NATIVE WEEK — matching exactly how Option C actually
        /// locked that game's real Projection row (WeeklyRankingsService step 2),
        /// not a single global week applied to every game. This is why the caller
        /// doesn't (and shouldn't) pass one week for the whole season: a week-13
        /// game's locked projection used week-12 information, not week-0's.
        ///
        /// Added 2026-09-03, same session as AnalyzePredictionMathAsync — the
        /// single-game version came first (Ohio State specifically); this is the
        /// natural next step once the pattern was proven out for one game.
        /// </summary>
        public async Task<List<SeasonPredictionMathGame>> AnalyzeSeasonPredictionMathAsync(
            int year, int teamId, double? hfaOverride = null, CancellationToken token = default)
        {
            var allYearGames = await _uow.Games.GetByYearAsync(year, token);
            var teamsDict    = await _uow.Teams.GetDictionaryByTeamIdAsync(token);

            var teamGames = allYearGames
                .Where(g => (g.HomeId == teamId || g.AwayId == teamId) &&
                            g.HomeId.HasValue && g.AwayId.HasValue)
                .OrderBy(g => g.Week)
                .ToList();

            var results = new List<SeasonPredictionMathGame>(teamGames.Count);

            foreach (var g in teamGames)
            {
                bool isHomeTeam = g.HomeId == teamId;
                int  opponentId = isHomeTeam ? g.AwayId!.Value : g.HomeId!.Value;
                char location   = g.NeutralSite == true ? 'N' : (isHomeTeam ? 'H' : 'A');

                // Each game locked using its OWN native week — see remarks above.
                var detail = await AnalyzePredictionMathByIdAsync(
                    year, teamId, opponentId, location, g.Week, hfaOverride, token);

                var opponentName = teamsDict.TryGetValue(opponentId, out var opp)
                    ? opp.TeamName : $"Team {opponentId}";

                results.Add(new SeasonPredictionMathGame(g.Week, opponentId, opponentName, location, detail));
            }

            return results;
        }

        /// <summary>
        /// DIAGNOSTIC — READ-ONLY. Shows every step CalculatePrediction's margin
        /// calculation actually goes through — SeedRating -> anchorUnit ->
        /// anchorRanking -> blendedRanking for both teams (via ExperimentalInertia
        /// RatingService.AnalyzeAnchorAsync), then the same GetStrengthDifferential /
        /// GetExpectedDistribution calls CalculatePrediction itself makes. Does NOT
        /// reproduce scoring/total-points/confidence — this is scoped to the margin
        /// question specifically. Uses the real _avgScoreDifferentialService and
        /// _blendedRating this class already has injected — no new dependencies,
        /// no second implementation of any of this math.
        ///
        /// Added 2026-09-03 to check Texas's 2026 week-0-locked schedule
        /// projections — several games showed tight margins and it wasn't possible
        /// to hand-verify them without the FULL FBS week's PowerRating distribution
        /// (liveMean/liveStdDev), which a manual spot-check can't safely
        /// approximate from a handful of teams.
        /// Example: teamName="Texas", opponentName="Ohio State", week=2 (locked
        /// using week 1's snapshot). For a whole season at once, use
        /// AnalyzeSeasonPredictionMathAsync instead — it resolves each game's own
        /// native week from the real schedule rather than requiring one call per
        /// opponent.
        /// </summary>
        public async Task<PredictionMathDetail> AnalyzePredictionMathAsync(
            int year, string teamName, string opponentName, char location, int week,
            double? hfaOverride = null, CancellationToken token = default)
        {
            var team     = await _uow.Teams.GetByNameAsync(teamName, token)
                           ?? throw new ArgumentException($"Team not found: {teamName}");
            var opponent = await _uow.Teams.GetByNameAsync(opponentName, token)
                           ?? throw new ArgumentException($"Team not found: {opponentName}");

            return await AnalyzePredictionMathByIdAsync(
                year, team.TeamId, opponent.TeamId, location, week, hfaOverride, token);
        }

        /// <summary>ID-based core shared by AnalyzePredictionMathAsync and AnalyzeSeasonPredictionMathAsync — no name lookups, no duplicated math.</summary>
        private async Task<PredictionMathDetail> AnalyzePredictionMathByIdAsync(
            int year, int teamId, int opponentId, char location, int week,
            double? hfaOverride, CancellationToken token)
        {
            var (teamDetail, oppDetail) = await _blendedRating.AnalyzeAnchorAsync(
                teamId, opponentId, year, week, token);

            var differential = _avgScoreDifferentialService.GetStrengthDifferential(
                (double)teamDetail.BlendedRanking, (double)oppDetail.BlendedRanking);

            var distribution = _avgScoreDifferentialService.GetExpectedDistribution(
                (double)teamDetail.BlendedRanking, (double)oppDetail.BlendedRanking);

            var expectedMarginAfterHfa = RatingCalculator.ApplyHomeField(
                distribution.ExpectedMargin, location == 'H', location == 'N',
                hfaOverride ?? _config.HomeFieldAdvantage);

            return new PredictionMathDetail(
                teamDetail, oppDetail, differential,
                distribution.ExpectedMargin, expectedMarginAfterHfa, distribution.StdDev,
                distribution.SampleSize, distribution.Reliability);
        }

        /// <summary>
        /// Sandbox: predicts a matchup between two teams from potentially different years.
        /// For a completed year, ratings are loaded from that year's true final
        /// TeamRecords values (see GetEndOfSeasonRatingsAsync) — not the K=4 blend
        /// used for live week-to-week predictions elsewhere in this class. For a
        /// year with any unplayed games remaining, ratings are instead loaded via
        /// the same live K=4 blend (ExperimentalInertiaRatingService.
        /// GetBlendedRatingsForWeekAsync) Rankings uses, anchored to each team's
        /// own actual last-played week — see the unplayed-weeks check below for why.
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

            // Any unplayed week for a year means TeamRecords for that year was last
            // synced from WeeklyRankings' MAX week row (TeamRecordRepository.
            // UpsertFromWeeklyRankingsAsync), which includes locked PROJECTIONS for
            // every not-yet-played week under Option C — not real results. Route
            // in-progress years through the same live K=4 blend Rankings uses
            // instead, so Sandbox reflects only games actually played. Completed
            // years keep the direct TeamRecords read (GetEndOfSeasonRatingsAsync) —
            // real results exist for every week there, so the season-max-week row
            // is legitimately final.
            //
            // The highest unplayed week is passed as GetBlendedRatingsForWeekAsync's
            // `week` — its capWeek = week - 1 only needs to exceed every team's
            // real lastPlayedWeek, which the highest currently-unplayed week always
            // does by definition. Self-adjusts as the schedule grows (e.g.
            // conference championships arriving at the postseason data drop push
            // the highest unplayed week higher automatically) — no hardcoded week
            // ceiling to maintain. GetUnplayedWeeksAsync returns ascending order,
            // so .Last() is the highest unplayed week.
            var teamUnplayedWeeks = await _uow.Games.GetUnplayedWeeksAsync(teamYear,     token);
            var oppUnplayedWeeks  = await _uow.Games.GetUnplayedWeeksAsync(opponentYear, token);

            var teamRecords = teamUnplayedWeeks.Any()
                ? await _blendedRating.GetBlendedRatingsForWeekAsync(teamYear, teamUnplayedWeeks.Last(), token)
                : await GetEndOfSeasonRatingsAsync(teamYear, token);

            var oppRecords = oppUnplayedWeeks.Any()
                ? await _blendedRating.GetBlendedRatingsForWeekAsync(opponentYear, oppUnplayedWeeks.Last(), token)
                : await GetEndOfSeasonRatingsAsync(opponentYear, token);

            if (!teamRecords.TryGetValue(team.TeamId,     out var teamRecord))
                throw new ArgumentException($"No ratings found for {teamName} in {teamYear}.");
            if (!oppRecords.TryGetValue(opponent.TeamId,  out var oppRecord))
                throw new ArgumentException($"No ratings found for {opponentName} in {opponentYear}.");

            var rivalries    = await _uow.Lookups.GetMatchupHistoriesAsync(token);
            // Average team score across both years for realistic scoring context
            var avgTeamScore    = await GetAverageTeamScoreAsync(Math.Min(teamYear, opponentYear), token);
            var allDifferentials = await GetAllDifferentialsAsync(token);

            return CalculatePrediction(
                teamRecord, oppRecord, team, opponent, 'N',
                rivalries, avgTeamScore, allDifferentials,
                Math.Max(teamYear, opponentYear), 0, null,
                applyWeeklyScoringAdjustments: false,
                isSandboxContext: true);
        }

        /// <summary>
        /// Predicts scores for multiple matchups in a single DB round-trip, as of a
        /// given week's data.
        ///
        /// `asOfWeek` is the week this projection is being made FROM — e.g. "using
        /// everything we know as of week 0" — and applies to every matchup in the
        /// batch, since they're all rated off the same K=4-blended snapshot of team
        /// strength for that week.
        ///
        /// Ratings are always read from the most recent COMPLETED week (asOfWeek - 1)
        /// — asOfWeek's own WeeklyRankings row doesn't exist yet at prediction time.
        ///
        /// Previously took a `useWeekAsLive` flag to instead read ratings from
        /// asOfWeek directly (for callers whose asOfWeek was itself an already-
        /// completed, already-persisted snapshot — a just-finished live week or a
        /// historical backfill snapshot). That flag's only two callers
        /// (ComputeAndSaveAsync step 17, and DeveloperService.BackfillProjections-
        /// StreamAsync) have both been removed/replaced (step 17 by Option C;
        /// BackfillProjectionsStreamAsync deleted as redundant with
        /// BackfillWeeklyRankings — confirmed via solution-wide Find All References,
        /// zero remaining callers passed true). Removed here rather than left dead.
        /// If a future caller needs the asOfWeek-is-already-live behavior again,
        /// reintroduce the flag rather than guessing — see git history for the
        /// removed branch.
        /// </summary>
        public async Task<List<GamePrediction>> PredictMatchups(
            int year, int asOfWeek, List<MatchupRequest> matchups,
            CancellationToken token = default)
        {
            var teams        = await _uow.Teams.GetDictionaryByNameAsync(token);
            var recordsById  = await GetRatingsForWeekAsync(year, asOfWeek, token);
            var rivalries    = await _uow.Lookups.GetMatchupHistoriesAsync(token);
            var avgTeamScore = await GetAverageTeamScoreAsync(year, token);
            var allDifferentials = await GetAllDifferentialsAsync(token);

            var predictions = new List<GamePrediction>();

            foreach (var matchup in matchups)
            {
                if (!teams.TryGetValue(matchup.TeamName,     out var team)      ||
                    !teams.TryGetValue(matchup.OpponentName, out var opponent))  continue;

                if (!recordsById.TryGetValue(team.TeamId,     out var teamRecord) ||
                    !recordsById.TryGetValue(opponent.TeamId, out var oppRecord))  continue;

                predictions.Add(CalculatePrediction(
                    teamRecord, oppRecord, team, opponent, matchup.Location,
                    rivalries, avgTeamScore, allDifferentials, year, matchup.Week, null));
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
            var allDifferentials = await GetAllDifferentialsAsync(token);

            var predictions = new List<GamePrediction>();

            foreach (var matchup in matchups)
            {
                if (!teams.TryGetValue(matchup.TeamName,     out var team)      ||
                    !teams.TryGetValue(matchup.OpponentName, out var opponent))  continue;

                if (!recordsById.TryGetValue(team.TeamId,     out var teamRecord) ||
                    !recordsById.TryGetValue(opponent.TeamId, out var oppRecord))  continue;

                predictions.Add(CalculatePrediction(
                    teamRecord, oppRecord, team, opponent, matchup.Location,
                    rivalries, avgTeamScore, allDifferentials, year, matchup.Week, hfaOverride));
            }

            return predictions.OrderByDescending(p => Math.Abs(p.ExpectedMargin)).ToList();
        }

        // ── Projection builder ────────────────────────────────────────────────────

        /// <summary>
        /// homeWinDiff/awayWinDiff: each team's real Wins - Losses as of the same
        /// pregame snapshot CalculatePrediction used for `prediction` — NOT derived
        /// from prediction itself. Caller's responsibility to source these from the
        /// same prior-week WeeklyRankings snapshot the rating pipeline used, same
        /// convention TierDiscountCalculator itself uses.
        ///
        /// homeTier/awayTier: "P4"/"G6"/"FCS"/"Other" from ConferenceTierService.
        /// Only a strict P4-vs-G6 pair triggers the tier discount below — FCS/Other on
        /// either side, or a same-tier P4-vs-P4/G6-vs-G6 matchup, leaves the
        /// prediction untouched.
        ///
        /// tierDiscountCoefficient: the season's row from
        /// TierDiscountCoefficients.GetLatestBySeasonAsync, or null if none exists yet
        /// for this season (e.g. season setup hasn't run the compute step) — treated
        /// the same as a same-tier matchup, no adjustment applied.
        ///
        /// Tier discount, when it applies: AdjustedMargin = K * (WinDiffT1 - D *
        /// WinDiffT2) + C, toward the P4 side (see TierDiscountCoefficient remarks).
        /// This is a downstream adjustment on top of CalculatePrediction's output —
        /// AvgScoreDifferentialService/CalculatePrediction itself is NOT touched, per
        /// the project's tier-agnostic constraint on that service. Split across both
        /// sides' raw scores (half added to T1, half subtracted from T2) so the score
        /// gap moves by the full adjustment while PredictedTotal is unaffected; the
        /// full adjustment is applied directly to the spread, keeping both
        /// representations consistent with each other.
        /// </summary>
        public static Projection BuildProjection(
            GamePrediction prediction,
            int gameId, int year, int week,
            int homeTeamId, int awayTeamId,
            int homeWinDiff, int awayWinDiff,
            string homeTier, string awayTier,
            TierDiscountCoefficient? tierDiscountCoefficient)
        {
            // Team-perspective → home/away mapping. Location is 'H' for a normal
            // home game and 'N' for neutral site — TeamName is always the actual
            // home team in both cases (see MatchupRequest construction in
            // WeeklyRankingsService/RatingComparisonService). Only 'A' means the
            // "team" in this GamePrediction is actually the away side. Using
            // "!= 'A'" instead of "== 'H'" fixes the neutral-site flip: previously
            // Location == 'N' fell into the away branch on all four mappings below.
            bool teamIsHomeSide = prediction.Location != 'A';

            var homePointsRaw = teamIsHomeSide
                ? prediction.PredictedTeamScore
                : prediction.PredictedOpponentScore;
            var awayPointsRaw = teamIsHomeSide
                ? prediction.PredictedOpponentScore
                : prediction.PredictedTeamScore;

            var rawSpread = teamIsHomeSide ? prediction.ExpectedMargin : -prediction.ExpectedMargin;

            // ── Tier discount — downstream adjustment only, see remarks above ──────
            bool homeIsP4 = homeTier == "P4", awayIsP4 = awayTier == "P4";
            bool homeIsG6 = homeTier == "G6", awayIsG6 = awayTier == "G6";
            bool isCrossTierP4VsG6 = (homeIsP4 && awayIsG6) || (homeIsG6 && awayIsP4);

            if (isCrossTierP4VsG6 && tierDiscountCoefficient != null)
            {
                var d = (double)tierDiscountCoefficient.WinDifferentialDiscount;
                var k = (double)tierDiscountCoefficient.PointsPerWinDifferential;
                var c = (double)tierDiscountCoefficient.CaliberGapPoints;

                bool homeIsT1 = homeIsP4;
                double winDiffT1 = homeIsT1 ? homeWinDiff : awayWinDiff;
                double winDiffT2 = homeIsT1 ? awayWinDiff : homeWinDiff;

                var adjustedMargin = k * (winDiffT1 - d * winDiffT2) + c; // toward T1

                var homeSignedAdjustment = homeIsT1 ? adjustedMargin : -adjustedMargin;

                homePointsRaw += homeSignedAdjustment / 2.0;
                awayPointsRaw -= homeSignedAdjustment / 2.0;
                rawSpread     += homeSignedAdjustment;
            }

            // Floor at 0 before rounding — mirrors the existing Math.Max(0, ...)
            // guard already applied to predictedTeamScore/predictedOppScore in
            // CalculatePrediction (display/sandbox path). BuildProjection's
            // persisted homePoints/awayPoints had no equivalent guard, which let
            // an extreme Ranking gap (e.g. a team's own compounding chain of
            // not-yet-played projections dragging its Ranking down week over
            // week — see 2026-08-20 Notre Dame diagnosis) round through to a
            // literal negative score in the stored Projection row. Companion
            // fix: ExperimentalInertiaRatingService.GetBlendedRatingsForWeekAsync
            // now pins its live rating source to the last actually-played week
            // instead of chaining week-1, which stops the underlying Ranking
            // gap from growing unbounded in the first place. This floor stays
            // in either way, as a hard guarantee no persisted score is ever
            // negative regardless of what upstream produces.
            homePointsRaw = Math.Max(0, homePointsRaw);
            awayPointsRaw = Math.Max(0, awayPointsRaw);

            var homePoints = (int)Math.Round(homePointsRaw, MidpointRounding.AwayFromZero);
            var awayPoints = (int)Math.Round(awayPointsRaw, MidpointRounding.AwayFromZero);

            // A tie is never a genuine model prediction — FBS games can't end
            // tied (OT rules). It's a Math.Round collision between two distinct
            // continuous scores that happened to land on the same integer.
            // Break it using IsTeamProjectedWinner — GamePrediction's own
            // documented source of truth for "who wins," derived from continuous
            // WinProbability rather than rounded scores, and already this
            // codebase's standard for projected-record/standings rollups (see
            // GamePrediction.IsTeamProjectedWinner remarks).
            //
            // NOTE: IsTeamProjectedWinner reflects CalculatePrediction's own
            // win probability and is NOT re-derived from the tier discount above —
            // a tie-break here can very rarely disagree with the tier-adjusted
            // score's own sign in a near-pick-'em cross-tier game. Acceptable: this
            // path only fires on an exact integer collision after rounding, an
            // already-rare edge case, and IsTeamProjectedWinner remains the
            // project's single documented source of truth for "who wins."
            if (homePoints == awayPoints)
            {
                bool teamIsHome = teamIsHomeSide;
                bool homeWins   = teamIsHome
                    ? prediction.IsTeamProjectedWinner
                    : !prediction.IsTeamProjectedWinner;

                if (homeWins) homePoints++; else awayPoints++;
            }

            var homeWinProb = teamIsHomeSide
                ? prediction.WinProbability
                : prediction.OpponentWinProbability;

            // Spread: rounded to the nearest tenth — matches the precision of
            // the real Vegas lines feed elsewhere in the app (VegasLines/Lines:
            // e.g. -2.8, -35.8, -39.0), not a half-point "avoid a push"
            // convention. Neither derived from homePoints - awayPoints (two
            // already-rounded integers can only ever land on a whole number —
            // the original complaint: every spread displaying as X.0) nor from
            // homePointsRaw - awayPointsRaw (PredictedTeamScore/
            // PredictedOpponentScore are a reliability-weighted BLEND, see
            // class remarks: total anchored on the AvgScoreDifferential
            // bucket's own AverageTotalPoints, corroborated by real PPG/PAG —
            // their difference isn't guaranteed to equal the model's actual
            // computed margin). Source is ExpectedMargin instead — the real
            // value (RatingCalculator.GetSmoothedExpectedMargin against
            // AvgScoreDifferential.AverageMargin, then HFA/rivalry-adjusted),
            // team-perspective like WinProbability, flipped to home/away the
            // same way homeWinProb is right above, PLUS the tier discount
            // adjustment applied above (rawSpread). Also deliberately NOT
            // affected by the tie-break above: that's purely a display-integer
            // artifact (two distinct continuous scores colliding on the same
            // rounded integer), not a real ~0 differential — ExpectedMargin
            // itself is untouched by it and rounds to 0.0 correctly in a
            // genuine pick-'em case.
            var spread = Math.Round(rawSpread, 1);

            return new Projection
            {
                GameId             = gameId,
                Year               = year,
                Week               = week,
                HomeTeamId         = homeTeamId,
                AwayTeamId         = awayTeamId,
                HomePoints         = homePoints,
                AwayPoints         = awayPoints,
                PredictedSpread    = (decimal)spread,
                // Total still derived from the tie-broken integer scores —
                // O/U is fine as a whole number, unlike spread.
                PredictedTotal     = homePoints + awayPoints,
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
        ///
        /// useWeekAsLive removed (was passed through unchanged) — confirmed via
        /// solution-wide Find All References that no caller anywhere passed true
        /// once BackfillProjectionsStreamAsync was deleted. See PredictMatchups
        /// remarks for the removal rationale (Finding #1 fix history).
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
            List<AvgScoreDifferential> allDifferentials,
            int year, int week,
            double? hfaOverride,
            bool applyWeeklyScoringAdjustments = true,
            bool isSandboxContext = false)
        {
            // Historical baseline — interpolated AverageMargin from AvgScoreDifferential,
            // keyed on Ranking. This is now the SOLE margin source. The previous ×10
            // PowerRating-delta correction has been removed: PowerRating is derived from
            // Ranking + SOS + Record (confirmed), so stacking it on top of a
            // Ranking-keyed bucket lookup was double-counting the same strength signal
            // this table already accounts for. See class remarks for the Week 20 2025
            // Texas/Alabama numbers that surfaced this.
            var distribution = _avgScoreDifferentialService.GetExpectedDistribution(
                (double)(teamRecord.Ranking ?? 0m),
                (double)(oppRecord.Ranking  ?? 0m));

            var expectedMargin = RatingCalculator.ApplyHomeField(
                distribution.ExpectedMargin, location == 'H', location == 'N',
                hfaOverride ?? _config.HomeFieldAdvantage);

            var normalizedT1 = Math.Min(team.TeamId, opponent.TeamId);
            var normalizedT2 = Math.Max(team.TeamId, opponent.TeamId);
            var rivalry      = rivalries.FirstOrDefault(
                r => r.Team1Id == normalizedT1 && r.Team2Id == normalizedT2);

            double  varianceMultiplier = RatingCalculator.RivalryVarianceMultiplierForDisplay(rivalry, distribution.StdDev);
            string? rivalryNote        = rivalry != null
                ? $"{rivalry.RivalryName} ({rivalry.RivalryTier})" : null;

            // Total points: the bucket's own historical AverageTotalPoints anchors the
            // number (same 60-year trust level as AverageMargin), corroborated — not
            // overridden — by these two teams' own PPG/PAG. Weighted by the bucket's
            // ReliabilityWeight: well-sampled buckets lean on history, thin buckets lean
            // more on this year's actual scoring. teamStatsImpliedTotal reconstructs
            // what the old PPG/PAG-only total implicitly was, now used as the
            // corroborating input instead of the sole source.
            var teamStatsImpliedTotal =
                ((double)teamRecord.AvgPointsScored + (double)oppRecord.AvgPointsAllowed) / 2.0 +
                ((double)oppRecord.AvgPointsScored  + (double)teamRecord.AvgPointsAllowed) / 2.0;

            var reliabilityWeight = distribution.Reliability;
            var totalPoints = (reliabilityWeight * distribution.AverageTotalPoints)
                             + ((1.0 - reliabilityWeight) * teamStatsImpliedTotal);

            var rawTeamScore = (totalPoints + expectedMargin) / 2.0;
            var rawOppScore  = (totalPoints - expectedMargin) / 2.0;

            // weekMultiplier represents real early/late-calendar-season scoring
            // variance and only means something for an actual calendar week.
            // PredictSandboxMatchupAsync always passes week = 0 for hypothetical,
            // possibly cross-year, neutral-site matchups — week 0 isn't "early
            // season" there, it's "no season at all" — so it must not fire. Gated
            // by an explicit flag rather than inferring intent from the week number
            // itself, which is exactly the kind of implicit-meaning bug this was.
            double weekMultiplier = applyWeeklyScoringAdjustments
                ? week switch { <= 4 => 1.05, >= 11 => 0.95, _ => 1.0 }
                : 1.0;

            // Rivalry-tier scoring adjustment only (still uses the RatingCalculator
            // hand-picked tier constants — flagged separately as a candidate to
            // replace with real MatchupHistory data, not addressed in this pass).
            //
            // Removed: a "ranked vs ranked" check that compared TeamRecord.Ranking
            // (the continuous ~0-1 Rating value) against <= 25. Ranking is never
            // greater than 25, so this was true for essentially every matchup and
            // never actually detected top-25 status — there's no ordinal rank field
            // on TeamRecord to check this correctly today. Removed rather than left
            // silently always-on.
            //
            // Removed: a week >= 15 postseason-compression multiplier — an unreliable
            // calendar proxy for "conference championship week," which doesn't land
            // on a fixed week number every season.
            double scoringAdjustment = RatingCalculator.RivalryScoringAdjustment(rivalry, distribution.AverageTotalPoints);

            var predictedTeamScore = Math.Max(0, rawTeamScore * weekMultiplier * scoringAdjustment);
            var predictedOppScore  = Math.Max(0, rawOppScore  * weekMultiplier * scoringAdjustment);

            // Real, interpolated historical stddev for this differential — no floor or
            // cap against the retired AvgScoreDelta constants. Rivalry variance
            // multiplier still applies (a rivalry genuinely is less predictable than the
            // baseline for that strength gap).
            var stdDev        = distribution.StdDev * varianceMultiplier;
            var marginOfError = stdDev;

            var confidence = BuildConfidence(
                allDifferentials, distribution, varianceMultiplier,
                teamRecord, oppRecord, expectedMargin);

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
                ExpectedMargin         = Math.Round(expectedMargin,     1),
                MarginOfError          = Math.Round(marginOfError,      1),
                RawStdDev              = stdDev,
                Confidence             = confidence.Tier,
                ConfidenceExplanation  = isSandboxContext ? confidence.Explanation : null,
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

        /// <summary>
        /// Fetches the full AvgScoreDifferential table once per service instance, used
        /// to rank a given matchup's stddev against the table's own real distribution
        /// for confidence tiering (see BuildConfidence). Same simple per-instance
        /// caching pattern as GetAverageTeamScoreAsync above — the table doesn't change
        /// mid-request.
        /// </summary>
        private async Task<List<AvgScoreDifferential>> GetAllDifferentialsAsync(CancellationToken token)
        {
            _cachedDifferentials ??= await _uow.Lookups.GetAvgScoreDifferentialsAsync(token);
            return _cachedDifferentials;
        }

        /// <summary>
        /// Confidence tier and footer explanation, built together from the same two
        /// data-derived signals so they can never disagree with each other:
        ///
        ///   1. Baseline volatility tier — where THIS matchup's stddev falls relative
        ///      to the percentile distribution of StdDevMargin across the FULL
        ///      AvgScoreDifferential table. Self-calibrating: the boundaries are
        ///      quartiles of the table's own real data, not guessed cutoffs, so they
        ///      move automatically as more historical data is added.
        ///
        ///   2. Game-specific corroboration — do these two teams' own OffensiveZScore/
        ///      DefensiveZScore edges point the same direction as the historical
        ///      margin. Both are already stdev-unit-consistent, so they're combined
        ///      unweighted (no tuning coefficient needed, unlike an earlier draft that
        ///      hand-picked 1.25/2.0 weights). SOS/PowerRating/Record/Win% are
        ///      deliberately excluded — they're already folded into the Ranking-keyed
        ///      margin itself, so including them here would be corroborating the
        ///      baseline with itself.
        ///
        /// The corroboration signal shifts the baseline tier one step up (agrees) or
        /// down (contradicts), but ONLY when the bucket's own ReliabilityWeight is at
        /// least 0.5 — half of the entity's own ReliabilityThreshold's worth of sample
        /// size. A thin bucket doesn't have enough of a trusted baseline for a single
        /// game's metric agreement to override in either direction.
        ///
        /// The Explanation text is Sandbox-only footer copy — deliberately never names
        /// a rivalry or implies a real scheduled game (Sandbox matchups are hypothetical
        /// and can pair any two team-seasons, so "the Iron Bowl" would be misleading
        /// even when the pairing happens to be one of the 52 curated rivalries). A
        /// curated pair's real historical data still shapes the NUMBERS (baselineStdDev
        /// vs effectiveStdDev diverge whenever RivalryVarianceMultiplierForDisplay
        /// actually moved something) — the copy just describes that divergence
        /// generically ("matchups between these two") rather than naming it.
        /// </summary>
        private static (string Tier, string Explanation) BuildConfidence(
            List<AvgScoreDifferential> allDifferentials,
            ExpectedGameDistribution distribution,
            double varianceMultiplier,
            TeamRecord teamRecord,
            TeamRecord oppRecord,
            double expectedMargin)
        {
            var baselineStdDev  = distribution.StdDev;
            var effectiveStdDev = distribution.StdDev * varianceMultiplier;

            var allStdDevs = allDifferentials
                .Select(b => (double)b.StdDevMargin)
                .OrderBy(x => x)
                .ToList();

            var baselineTier = TierFromPercentile(allStdDevs, effectiveStdDev);

            var netZEdge =
                ((double)teamRecord.OffensiveZScore - (double)oppRecord.DefensiveZScore) -
                ((double)oppRecord.OffensiveZScore  - (double)teamRecord.DefensiveZScore);

            var marginSign = Math.Sign(expectedMargin);
            var zEdgeSign  = Math.Sign(netZEdge);

            bool corroborates = marginSign != 0 && zEdgeSign != 0 && marginSign == zEdgeSign;
            bool contradicts  = marginSign != 0 && zEdgeSign != 0 && marginSign != zEdgeSign;

            bool reliableEnoughToAdjust = distribution.Reliability >= 0.5;

            var tier = baselineTier;
            if (reliableEnoughToAdjust && corroborates) tier = ShiftTier(baselineTier, +1);
            if (reliableEnoughToAdjust && contradicts)  tier = ShiftTier(baselineTier, -1);

            // A curated rivalry only counts as "adjusted" if it actually moved the
            // number — RivalryVarianceMultiplierForDisplay returns exactly 1.00 for
            // any non-curated pair (~750+ of them), so this is really asking "was this
            // one of the 52," not re-deciding anything.
            bool rivalryAdjusted = Math.Abs(varianceMultiplier - 1.0) > 0.0001;

            var explanation = BuildConfidenceExplanation(
                tier, baselineStdDev, effectiveStdDev, rivalryAdjusted,
                reliableEnoughToAdjust, corroborates, contradicts);

            return (tier, explanation);
        }

        /// <summary>
        /// Sandbox-only footer copy. See BuildConfidence remarks for why rivalry names
        /// are never mentioned even when a curated pair's real data drove the numbers.
        /// </summary>
        private static string BuildConfidenceExplanation(
            string tier,
            double baselineStdDev,
            double effectiveStdDev,
            bool rivalryAdjusted,
            bool reliableEnoughToAdjust,
            bool corroborates,
            bool contradicts)
        {
            var lead = $"{tier} confidence. ";
            string volatilityClause;

            if (rivalryAdjusted)
            {
                var direction = effectiveStdDev > baselineStdDev ? "more volatile" : "more predictable";
                volatilityClause =
                    $"Matchups between these two have historically run {direction} than a typical game " +
                    $"at this strength gap — about \u00b1{effectiveStdDev:F0} points compared to the usual " +
                    $"\u00b1{baselineStdDev:F0}.";
            }
            else
            {
                var descriptor = tier switch
                {
                    "Very Low" or "Low" => "evenly matched",
                    "High"               => "lopsided",
                    _                    => "a moderate strength gap"
                };

                var trailing = descriptor switch
                {
                    "evenly matched" => " \u2014 closer games are inherently less predictable than blowouts.",
                    "lopsided"        => " \u2014 blowouts are simply more predictable than close games.",
                    _                 => "."
                };

                volatilityClause =
                    $"In a matchup this {descriptor}, historical outcomes have varied by about " +
                    $"\u00b1{effectiveStdDev:F0} points from the projection{trailing}";
            }

            if (!reliableEnoughToAdjust)
                return lead + volatilityClause;

            string metricsClause;
            if (corroborates)
            {
                metricsClause = rivalryAdjusted
                    ? " This year's offensive and defensive numbers reinforce that lean."
                    : " This year's offensive and defensive numbers for both teams support that projection.";
            }
            else if (contradicts)
            {
                metricsClause = tier == "Very Low"
                    ? " This year's offensive and defensive numbers actually point the other way \u2014 " +
                      "treat this projection as a coin flip at best."
                    : " This year's offensive and defensive numbers actually lean the other way, so " +
                      "treat this margin with extra caution.";
            }
            else
            {
                metricsClause = "";
            }

            return lead + volatilityClause + metricsClause;
        }

        private static readonly string[] TierOrder = { "Very Low", "Low", "Medium", "High" };

        /// <summary>
        /// percentile = fraction of all buckets in the table with StdDevMargin at or
        /// below this matchup's stddev. Low percentile = this matchup is unusually
        /// predictable relative to the whole table = High confidence. High percentile =
        /// unusually volatile = Very Low confidence. Quartile boundaries, not fixed
        /// point values — they're relative to whatever the table's real distribution is.
        /// </summary>
        private static string TierFromPercentile(List<double> sortedStdDevs, double stdDev)
        {
            if (sortedStdDevs.Count == 0) return "Medium";

            var countAtOrBelow = sortedStdDevs.Count(x => x <= stdDev);
            var percentile = (double)countAtOrBelow / sortedStdDevs.Count;

            return percentile switch
            {
                <= 0.25 => "High",
                <= 0.50 => "Medium",
                <= 0.75 => "Low",
                _       => "Very Low"
            };
        }

        private static string ShiftTier(string tier, int steps)
        {
            var idx = Array.IndexOf(TierOrder, tier);
            if (idx < 0) return tier;
            idx = Math.Clamp(idx + steps, 0, TierOrder.Length - 1);
            return TierOrder[idx];
        }
    }
}
