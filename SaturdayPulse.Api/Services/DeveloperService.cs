using Microsoft.Extensions.Options;
using SaturdayPulse.Api.Contracts.Responses;
using SaturdayPulse.Configuration;
using SaturdayPulse.Contracts;
using SaturdayPulse.Contracts.Requests;
using SaturdayPulse.Contracts.Responses;
using SaturdayPulse.Core.Progress;
using SaturdayPulse.Interfaces;
using SaturdayPulse.Models;
using SaturdayPulse.Utilities;
using System.Runtime.CompilerServices;

namespace SaturdayPulse.Services
{
    /// <summary>
    /// Encapsulates all data-access and business logic for development/admin operations.
    /// Pass 2 complete: all EF queries moved to repositories.
    /// No direct _context references remain.
    /// </summary>
    public class DeveloperService
    {
        // Points/game shift per 1.0 std dev of year-over-year roster talent change,
        // applied in InitializeSeasonAsync to seedRows' AvgScored/AvgAllowed before
        // those feed the scoring-based OffensiveZScore/DefensiveZScore computation.
        private const double RosterZScoreToPointsFactor = 4.0;

        private readonly IUnitOfWork               _uow;
        private readonly IGameDataService          _gameDataService;
        private readonly RollingAverageService     _rollingAverageService;
        private readonly RosterCapacityService     _rosterCapacityService;
        private readonly ScoreDeltaCalculator      _scoreDeltaCalculator;
        private readonly MatchupHistoryCalculator  _matchupHistoryCalculator;
        private readonly TierDiscountCalculator    _tierDiscountCalculator;
        private readonly AnchorBlendCalculator     _anchorBlendCalculator;
        private readonly WeeklyRankingsService     _weeklyRankingsService;
        private readonly GamePredictionService     _predictionService;
        private readonly ConferenceTierService     _tierService;
        private readonly MetricsConfiguration      _config;
        private readonly ILogger<DeveloperService> _logger;

        public DeveloperService(
            IUnitOfWork uow,
            IGameDataService gameDataService,
            RollingAverageService rollingAverageService,
            RosterCapacityService rosterCapacityService,
            ScoreDeltaCalculator scoreDeltaCalculator,
            MatchupHistoryCalculator matchupHistoryCalculator,
            TierDiscountCalculator tierDiscountCalculator,
            AnchorBlendCalculator anchorBlendCalculator,
            WeeklyRankingsService weeklyRankingsService,
            GamePredictionService predictionService,
            ConferenceTierService tierService,
            IOptions<MetricsConfiguration> config,
            ILogger<DeveloperService> logger)
        {
            _uow                      = uow;
            _gameDataService          = gameDataService;
            _rollingAverageService    = rollingAverageService;
            _rosterCapacityService    = rosterCapacityService;
            _scoreDeltaCalculator     = scoreDeltaCalculator;
            _matchupHistoryCalculator = matchupHistoryCalculator;
            _tierDiscountCalculator   = tierDiscountCalculator;
            _anchorBlendCalculator    = anchorBlendCalculator;
            _weeklyRankingsService    = weeklyRankingsService;
            _predictionService        = predictionService;
            _tierService              = tierService;
            _config                   = config.Value;
            _logger                   = logger;
        }

        public Task<int> LoadConferencesAsync(CancellationToken token = default)
            => _gameDataService.LoadConferencesAsync(token);

        public Task<int> LoadTeamsAsync(int? year, CancellationToken token = default)
            => _gameDataService.LoadTeamsAsync(year, token);

        public Task<int> LoadTeamsBulkAsync(int startYear, CancellationToken token = default)
            => _gameDataService.LoadTeamsBulkAsync(startYear, token);

        public IAsyncEnumerable<ProgressUpdate> LoadTeamsBulkStreamAsync(int startYear, CancellationToken token = default)
            => _gameDataService.LoadTeamsBulkStreamAsync(startYear, token);

        public Task<int> AssignPostseasonWeeksAsync(int year, CancellationToken token = default)
            => _gameDataService.AssignPostseasonWeeksAsync(year, token);

        public Task<int> AssignPostseasonWeeksBulkAsync(int startYear, CancellationToken token = default)
            => _gameDataService.AssignPostseasonWeeksBulkAsync(startYear, token);

        public Task<int> LoadGamesAsync(int year, int? week, CancellationToken token = default)
            => _gameDataService.LoadGamesAsync(year, week, token);

        public Task<int> LoadGamesBulkAsync(int startYear, CancellationToken token = default)
            => _gameDataService.LoadGamesBulkAsync(startYear, token);

        public IAsyncEnumerable<ProgressUpdate> LoadGamesBulkStreamAsync(int startYear, CancellationToken token = default)
            => _gameDataService.LoadGamesBulkStreamAsync(startYear, token);

        public Task<int> LoadLinesAsync(int year, int week, CancellationToken token = default)
            => _gameDataService.LoadLinesAsync(year, week, token);

        public Task<int> LoadLinesBulkAsync(int startYear, CancellationToken token = default)
            => _gameDataService.LoadLinesBulkAsync(startYear, token);

        public IAsyncEnumerable<ProgressUpdate> LoadLinesBulkStreamAsync(int startYear, CancellationToken token = default)
            => _gameDataService.LoadLinesBulkStreamAsync(startYear, token);

        public Task<int> BuildAvgScoreDifferentialsAsync(int startYear, CancellationToken token = default)
            => _gameDataService.BuildAvgScoreDifferentialsAsync(startYear, token);

        public Task<int> BuildTeamsConferenceHistoryAsync(int startYear, CancellationToken token = default)
           => _gameDataService.BuildTeamsConferenceHistoryAsync(startYear, token);

        public Task<PortalCoverageResult> GetPortalCoverageAsync(CancellationToken token = default)
            => _gameDataService.GetPortalCoverageAsync(token);

        public Task<int> WeeklyRefreshAsync(int year, int week, CancellationToken token = default)
            => _gameDataService.WeeklyRefreshAsync(year, week, token);

        public Task<int> LoadPortalAsync(int season, CancellationToken token = default)
            => _gameDataService.LoadPortalAsync(season, token);

        public Task<int> LoadRosterCapacityRosterAsync(int season, CancellationToken token = default)
            => _gameDataService.LoadRosterCapacityRosterAsync(season, token);

        public Task<int> LoadRosterCapacityStatsAsync(int season, CancellationToken token = default)
            => _gameDataService.LoadRosterCapacityStatsAsync(season, token);

        public Task<int> LoadRosterCapacityCoachesAsync(int year, CancellationToken token = default)
            => _gameDataService.LoadRosterCapacityCoachesAsync(year, token);

        public Task<int> LoadRosterCapacityRecruitingAsync(int year, CancellationToken token = default)
            => _gameDataService.LoadRosterCapacityRecruitingAsync(year, token);

        // Loads the recruiting class for a year and immediately joins RecruitRating into
        // RosterPlayers for that same year. Requires that year's roster already loaded.
        public Task<(int RecruitsLoaded, int RatingsApplied)> LoadAndApplyRosterCapacityRecruitingAsync(
            int year, CancellationToken token = default)
            => _gameDataService.LoadAndApplyRosterCapacityRecruitingAsync(year, token);

        public Task<(int PortalLoaded, int RatingsApplied)> LoadAndApplyPortalRatingsAsync(
            int season, CancellationToken token = default)
            => _gameDataService.LoadAndApplyPortalRatingsAsync(season, token);

        // Convenience wrapper — loads roster for both T and T-1 in one call, since
        // RosterCapacityService always needs both snapshots together.
        public async Task<(int CurrentCount, int PriorCount)> LoadRosterCapacityBothSeasonsAsync(
            int currentSeason, CancellationToken token = default)
        {
            var currentCount = await _gameDataService.LoadRosterCapacityRosterAsync(currentSeason, token);
            await Task.Delay(300, token); // rate limit, matches existing bulk-load pattern
            var priorCount = await _gameDataService.LoadRosterCapacityRosterAsync(currentSeason - 1, token);
            return (currentCount, priorCount);
        }

        public Task<int> LoadPortalBulkAsync(int startSeason, CancellationToken token = default)
            => _gameDataService.LoadPortalBulkAsync(startSeason, token);

        public Task<int> ComputePortalMetricsAsync(int season, CancellationToken token = default)
            => _rosterCapacityService.ComputeZRosterAsync(season, token);

        public Task<int> ComputePortalMetricsBulkAsync(CancellationToken token = default)
            => _rosterCapacityService.ComputeZRosterBulkAsync(token);

        // ── Rolling Averages ──────────────────────────────────────────────────────

        public async Task<BackfillResult> BackfillRollingAveragesAsync(int? startYear, CancellationToken token)
        {
            var allRecords = await _uow.TeamRecords.GetSinceYearWithTeamsAsync(1960, token);
            var years      = allRecords.Select(tr => (int)tr.Year).Distinct().OrderBy(y => y).ToList();

            if (startYear.HasValue)
                years = years.Where(y => y >= startYear.Value).ToList();

            if (!years.Any())
                throw new InvalidOperationException("No TeamRecords found matching the criteria.");

            _logger.LogInformation("Backfilling rolling averages for {Count} years...", years.Count);

            int processed = 0;
            foreach (var year in years)
            {
                await _rollingAverageService.ComputeAndPersistAsync(year, week: null, token);
                processed++;
                _logger.LogInformation("Rolling averages complete: {Year} ({Done}/{Total})", year, processed, years.Count);
            }

            return new BackfillResult("Backfill complete.", processed, startYear);
        }

        public async Task<object> CalculateRollingAveragesAsync(int? year, int? week, CancellationToken token)
        {
            var targetYear = year ?? DateTime.Now.Year;
            await _rollingAverageService.ComputeAndPersistAsync(targetYear, week, token);
            return new
            {
                message        = $"Rolling averages computed for {targetYear}" +
                                 (week.HasValue ? $" week {week.Value}" : " (preseason)"),
                year           = targetYear, week, liveSwapActive = week.HasValue
            };
        }
        public async Task<int> SetSeasonTypeAsync(List<int> gameIds, string seasonType, CancellationToken token = default)
            => await _gameDataService.SetSeasonTypeAsync(gameIds, seasonType, token);

        // ── Team Records and Metrics ──────────────────────────────────────────────

        public Task UpdateTeamRecordsAsync(int? year)
            => _gameDataService.UpdateTeamRecordsAsync(year);

        // SetSOSAsync / CalculatePowerRatingsAsync / CalculateRankingsAsync /
        // RecalculateMetricsAsync / BackfillAllMetricsAsync removed —
        // TeamMetricsService deleted entirely. Its setSOS/calculatePowerRatings/
        // calculateRankings/backfillAllMetrics endpoints had no callers, and
        // updateWeeklyMetrics's output was silently overwritten by
        // WeeklyRankingsService.ComputeAndSaveAsync's own upsert one step later
        // in RunWeeklyRefreshAsync — plus its PowerRating calc used
        // GetGameParticipantsAsync unfiltered by played status, so unplayed
        // games counted as 0-0 results whenever it did run. WeeklyRankingsService
        // is the single source of truth for SOS/PowerRating/Ranking now.

        // ── Score Deltas and Rivalries ────────────────────────────────────────────

        public async Task<RecalculateScoreDeltasResult> RecalculateScoreDeltasAsync(CancellationToken token)
        {
            await _scoreDeltaCalculator.UpdateAvgScoreDeltasTableAsync();
            var deltas = await _uow.Lookups.GetAvgScoreDeltasAsync(token);
            return new RecalculateScoreDeltasResult(
                "Score deltas recalculated successfully", deltas.Count,
                "5% win percentage increments",
                "Predictions will now use updated delta statistics");
        }

        public async Task<RecreateTableResult> RecreateAvgScoreDeltasTableAsync(CancellationToken token)
        {
            await _uow.Lookups.ClearAvgScoreDeltasAsync(token);
            _logger.LogInformation("AvgScoreDeltas table cleared");
            await _scoreDeltaCalculator.UpdateAvgScoreDeltasTableAsync();
            var deltas = await _uow.Lookups.GetAvgScoreDeltasAsync(token);
            return new RecreateTableResult("AvgScoreDeltas table recreated successfully", deltas.Count, "Table cleared and repopulated");
        }

        public async Task<MatchupHistoriesResult> CalculateMatchupHistoriesAsync()
        {
            var count = await _matchupHistoryCalculator.CalculateAllMatchupHistories();
            return new MatchupHistoriesResult(
                "Matchup histories calculated successfully", count, 50,
                "Matchup-specific variance will now be used in predictions");
        }

        // ── Analytics and Diagnostics ─────────────────────────────────────────────

        /// <summary>
        /// Method B — MOV Variance Test (two-parameter). Diagnostic-only: computes and
        /// returns the fit without persisting anything. throughYear lets a caller test
        /// "as of a past season" behavior; omit for the live default (everything played
        /// so far). See TierDiscountCalculator's remarks for the full methodology.
        /// </summary>
        public Task<TierDiscountAnalysisResult> CalculateTierDiscountAsync(
            int startYear = 1965, int? throughYear = null, CancellationToken token = default)
            => _tierDiscountCalculator.CalculateAsync(startYear, throughYear, token);

        /// <summary>
        /// Computes and persists a new TierDiscountCoefficients row for `season`, using
        /// only games played through season - 1. Intended to run BEFORE
        /// InitializeSeason in RunSeasonSetupAsync. Returns null (no row persisted) if
        /// there's no usable data for that season yet — see
        /// TierDiscountCalculator.ComputeAndPersistCoefficientsAsync remarks.
        /// </summary>
        public Task<TierDiscountCoefficient?> ComputeTierDiscountCoefficientsAsync(
            int season, int startYear = 1965, CancellationToken token = default)
            => _tierDiscountCalculator.ComputeAndPersistCoefficientsAsync(season, startYear, token);

        /// <summary>
        /// Backfills AnchorBlendCoefficients for every season from startSeason through
        /// the most recent season with played data (or throughSeason, if given).
        /// Returns (Persisted, Skipped) — Skipped seasons had no usable prior-year data
        /// (expected for the earliest seasons of a full historical backfill, not an    
        /// error).
        /// </summary>
        public Task<(int Persisted, int Skipped)> ComputeTierDiscountCoefficientsBulkAsync(
            int startSeason, int? throughSeason = null, int startYear = 2021, CancellationToken token = default)
            => _tierDiscountCalculator.ComputeAndPersistCoefficientsBulkAsync(startSeason, throughSeason, startYear, token);

        /// <summary>
        /// Computes and persists a new AnchorBlendCoefficients row for `season`, using
        /// only games played through season - 1. Intended to run BEFORE
        /// InitializeSeason in RunSeasonSetupAsync. Returns null (no row persisted) if
        /// there's no usable data for that season yet — see
        /// AnchorBlendCalculator.ComputeAndPersistCoefficientsAsync remarks.
        /// </summary>
        public async Task<AnchorBlendCoefficient?> ComputeAnchorBlendCoefficientsAsync(
            int season, int windowYears = AnchorBlendCalculator.DefaultWindowYears, CancellationToken token = default)
            => await _anchorBlendCalculator.ComputeAndPersistCoefficientsAsync(season, windowYears, token);

        /// <summary>
        /// Backfills AnchorBlendCoefficients for every season from startSeason through
        /// the most recent season with played data (or throughSeason, if given).
        /// Returns (Persisted, Skipped) — Skipped seasons had no usable prior-year data
        /// (expected for the earliest seasons of a full historical backfill, not an
        /// error).
        /// </summary>
        public Task<(int Persisted, int Skipped)> ComputeAnchorBlendCoefficientsBulkAsync(
            int startSeason, int? throughSeason = null,
            int windowYears = AnchorBlendCalculator.DefaultWindowYears, CancellationToken token = default)
            => _anchorBlendCalculator.ComputeAndPersistCoefficientsBulkAsync(startSeason, throughSeason, windowYears, token);

        public async Task<AnalyticsResult> GetAnalyticsAsync(int? startYear, int? endYear, CancellationToken token)
        {
            var records = await _uow.TeamRecords.QueryAsync(
                startYear: startYear, endYear: endYear, limit: int.MaxValue, token: token);

            records = records.Where(tr => tr.PowerRating != null).ToList();

            var overperformers = records
                .Where(tr => tr.Wins > (tr.CombinedSOS ?? 0) * 12)
                .OrderByDescending(tr => tr.Wins - (double?)(tr.CombinedSOS ?? 0) * 12)
                .Take(10)
                .Select(tr => (object)new
                {
                    tr.Year, TeamName = tr.Teams?.TeamName, Record = $"{tr.Wins}-{tr.Losses}",
                    tr.CombinedSOS, tr.PowerRating,
                    Overperformance = tr.Wins - (tr.CombinedSOS ?? 0) * 12
                });

            var underperformers = records
                .Where(tr => tr.Wins < (tr.CombinedSOS ?? 0) * 12)
                .OrderBy(tr => tr.Wins - (double?)(tr.CombinedSOS ?? 0) * 12)
                .Take(10)
                .Select(tr => (object)new
                {
                    tr.Year, TeamName = tr.Teams?.TeamName, Record = $"{tr.Wins}-{tr.Losses}",
                    tr.CombinedSOS, tr.PowerRating,
                    Underperformance = (tr.CombinedSOS ?? 0) * 12 - tr.Wins
                });

            return new AnalyticsResult(
                records.Count,
                $"{startYear ?? records.Min(r => r.Year)}-{endYear ?? records.Max(r => r.Year)}",
                overperformers, underperformers,
                records.Average(r => (double?)r.PowerRating),
                records.Average(r => (double?)r.CombinedSOS));
        }

        public async Task<TeamGameAnalysisResult> AnalyzeTeamGamesAsync(int teamId, int? year, CancellationToken token)
        {
            var targetYear     = year ?? DateTime.Now.Year;
            var allGames       = await _uow.Games.GetByYearAsync(targetYear, token);
            var teamGames      = allGames
                .Where(g => g.HomeId == teamId || g.AwayId == teamId)
                .OrderBy(g => g.Week).ToList();

            var teamRecords    = await _uow.TeamRecords.GetByYearAsync(targetYear, token);
            var winsLookup     = teamRecords.ToDictionary(tr => tr.TeamID, tr => (int)tr.Wins);
            var lossesLookup   = teamRecords.ToDictionary(tr => tr.TeamID, tr => (int)tr.Losses);
            var avgScoreDeltas = await _uow.Lookups.GetAvgScoreDeltasAsync(token);
            var hfa            = _config.HomeFieldAdvantage;

            var analysis = teamGames.Select(g =>
            {
                bool isWinner       = g.HomeId == teamId && g.HomePoints > g.AwayPoints;
                var teamPoints      = g.HomeId == teamId ? g.HomePoints : g.AwayPoints;
                var oppPoints       = g.HomeId == teamId ? g.AwayPoints : g.HomePoints;
                var oppId           = g.HomeId == teamId ? g.AwayId : g.HomeId;
                var delta           = teamPoints - oppPoints;
                bool isHomeTeam     = g.HomeId == teamId;
                var locationDisplay = isHomeTeam ? "Home" : g.NeutralSite ? "Neutral" : "Away";
                var result          = isWinner ? "W" : "L";
                var opponentName    = isWinner ? g.AwayName : g.HomeName;

                var teamWins   = winsLookup.GetValueOrDefault(teamId, 0);
                var teamLosses = lossesLookup.GetValueOrDefault(teamId, 0);
                var oppWins    = winsLookup.GetValueOrDefault((int)oppId,   0);
                var oppLosses  = lossesLookup.GetValueOrDefault((int)oppId, 0);

                var teamWinPct = RatingCalculator.BucketWinPct(teamWins, teamWins + teamLosses);
                var oppWinPct  = RatingCalculator.BucketWinPct(oppWins,  oppWins  + oppLosses);
                var maxWinPct  = Math.Max(teamWinPct, oppWinPct);
                var minWinPct  = Math.Min(teamWinPct, oppWinPct);

                var asd = avgScoreDeltas.FirstOrDefault(a => a.Team1WinPct == maxWinPct && a.Team2WinPct == minWinPct);

                double zScore = 0.0, expectedDelta = 0.0, homeAdjustment = 0.0;

                if (asd != null && asd.StDevP != 0)
                {
                    expectedDelta = (double)asd.AverageScoreDelta;
                    var expectedFromTeam = RatingCalculator.ExpectedFromPerspective(expectedDelta, teamWinPct, oppWinPct);

                    if (isHomeTeam)         { expectedFromTeam += hfa; homeAdjustment =  hfa; }
                    else if (g.NeutralSite) { expectedFromTeam -= hfa; homeAdjustment = -hfa; }

                    zScore = (double)((delta - expectedFromTeam) / (double)asd.WeightedStdDev);
                }

                var baseExpected     = teamWins >= oppWins ? expectedDelta : -expectedDelta;
                var adjustedExpected = baseExpected + homeAdjustment;

                return (object)new
                {
                    g.Week, OpponentName = opponentName, Location = locationDisplay, result, delta,
                    TeamFinalWins         = teamWins, OppFinalWins = oppWins,
                    BaseExpectedDelta     = Math.Round(baseExpected,     1),
                    HomeAdjustment        = Math.Round(homeAdjustment,   1),
                    AdjustedExpectedDelta = Math.Round(adjustedExpected, 1),
                    ActualDelta           = delta,
                    Difference            = Math.Round((double)delta - adjustedExpected, 1),
                    ZScore                = Math.Round(zScore, 3),
                    Performance           = zScore > _config.DominantPerformanceThreshold ? "Dominant"
                                          : zScore > _config.UnderperformedThreshold ? "Expected"
                                          : "Underperformed"
                };
            }).ToList();

            var avgZScore  = analysis.Average(a => ((dynamic)a).ZScore);
            var teamRecord = await _uow.TeamRecords.GetByTeamAndYearAsync(teamId, targetYear, token);

            return new TeamGameAnalysisResult(
                teamId, targetYear, $"{teamRecord?.Wins}-{teamRecord?.Losses}",
                teamRecord?.CombinedSOS, Math.Round(avgZScore, 4), teamRecord?.PowerRating,
                Math.Round(avgZScore * (double)(teamRecord?.CombinedSOS ?? 1.0m), 4),
                analysis);
        }

        public async Task<TrendsResult> CalculateTrendsAsync(int? teamId, int? year, CancellationToken token)
        {
            var targetYear = year ?? DateTime.Now.Year;
            var records    = await _uow.TeamRecords.GetByYearWithTeamsAsync(targetYear, token);
            records        = records.Where(tr => tr.PowerRating != null).ToList();

            if (teamId.HasValue)
                records = records.Where(tr => tr.TeamID == teamId.Value).ToList();

            var trends = records.Select(tr => (object)new
            {
                TeamId                = tr.TeamID, TeamName = tr.Teams?.TeamName,
                tr.Year, Record       = $"{tr.Wins}-{tr.Losses}",
                tr.PowerRating, tr.CombinedSOS, tr.Ranking,
                WinPercentage         = (decimal)tr.Wins / (tr.Wins + tr.Losses),
                ProjectedFinalRanking = tr.Ranking,
                Trend                 = tr.PowerRating > 0.02m ? "Ascending"
                                      : tr.PowerRating < -0.02m ? "Descending"
                                      : "Stable"
            }).ToList();

            return new TrendsResult(targetYear, trends.Count, trends);
        }

        // ── Season Initialization ─────────────────────────────────────────────────

        /// <summary>
        /// Initializes week 0 for a new season — NOT a copy of the prior season's final
        /// WeeklyRankings row. Every field is either the correct "no games played yet"
        /// value for its own live formula, or a genuine cross-year-normalized historical
        /// estimate. See conversation history for the field-by-field reasoning; the prior
        /// version copied Wins/Losses/PointsFor/PointsAgainst/BaseSOS/SubSOS/CombinedSOS/
        /// PowerRating/Ranking/OverallRank/TierRank/OffensiveZScore/DefensiveZScore
        /// verbatim from last season's LAST week, which mixed season-cumulative counters,
        /// schedule-specific SOS, and non-cross-year-comparable raw PowerRating into a
        /// row that represented a season with zero games played.
        ///
        /// Ordering note: TrendRating (needed for PowerRating below) doesn't exist for
        /// `year` until RollingAverageService.ComputeAndPersistAsync runs, which itself
        /// requires TeamRecords rows for `year` to already exist. Resolved with three
        /// passes: (1) placeholder week-0 WeeklyRankings rows with correct season-counter
        /// values (zero) so TeamRecords can be upserted from them; (2) run
        /// RollingAverageService, which computes Trend from PRIOR years only — never
        /// reads this year's just-created placeholder PowerRating; (3) overwrite the
        /// placeholder PowerRating/AvgPointsScored/AvgPointsAllowed/Ranking/OverallRank/
        /// TierRank/OffensiveZScore/DefensiveZScore/OffensiveRank/DefensiveRank on both
        /// WeeklyRankings and TeamRecords using the now-available TrendRating and
        /// weighted historical scoring averages.
        /// </summary>
        public async Task<object> InitializeSeasonAsync(int year, CancellationToken token = default)
        {
            // Check if week 0 already exists for this year.
            var existing = await _uow.WeeklyRankings.GetByYearAndWeekAsync(year, 0, token);
            if (existing.Any())
            {
                _logger.LogInformation("Season {Year} already initialized — week 0 exists.", year);
                return new { message = $"Season {year} already initialized.", year, week = 0 };
            }

            // Confirm we have at least one prior year of WeeklyRankings to build history
            // from — same guard the old version had, just checked without needing a
            // specific "last snapshot" row (nothing here copies from one anymore).
            var snapshots = await _uow.WeeklyRankings.GetDistinctYearWeeksAsync(token);
            if (!snapshots.Any(s => year == 1965 || s.Year == year - 1))
                throw new InvalidOperationException(
                    $"No WeeklyRankings found for {year - 1}. Run backfillWeeklyRankings first.");

            // ── TODO: Apply draft score adjustments here ──────────────────────────
            // For each team, incorporate draft pick history into the Pedigree
            // component. Load from DraftScore table once that pipeline is built.
            // ─────────────────────────────────────────────────────────────────────

            var allTeams = await _uow.Teams.GetAllAsync(token);
            var fbsTeams = allTeams
                .Where(t => string.Equals(t.Division, "fbs", StringComparison.OrdinalIgnoreCase))
                .ToList();

            // ── Pass 1: placeholder week-0 rows, correct season-counters (zero) ────────
            // PowerRating/AvgPointsScored/AvgPointsAllowed/Ranking/etc. written as 0 here
            // — not final values, just enough for TeamRecords to be upserted from this
            // row in the next step. Overwritten for real in Pass 3.
            foreach (var t in fbsTeams)
            {
                await _uow.WeeklyRankings.AddAsync(new WeeklyRanking
                {
                    TeamID = t.TeamId,
                    Year = (short)year,
                    Week = 0,
                    Wins = 0,
                    Losses = 0,
                    PointsFor = 0,
                    PointsAgainst = 0,
                    BaseSOS = 0,
                    SubSOS = 0,
                    CombinedSOS = 0,
                    PowerRating = 0,
                    Ranking = 0,
                    OverallRank = 0,
                    TierRank = 0,
                    AvgPointsScored = 0,
                    AvgPointsAllowed = 0,
                    OffensiveZScore = 0,
                    DefensiveZScore = 0,
                    OffensiveRank = 0,
                    DefensiveRank = 0
                }, token);
            }
            await _uow.SaveChangesAsync(token);

            // Seed TeamRecords rows for `year` so RollingAverageService has something to
            // write SeedRating/TrendRating/PedigreeRating onto. Safe to source from the
            // placeholder week-0 row above — RollingAverageService.Compute never reads
            // this year's PowerRating for Trend/Pedigree (pure historical); Seed's
            // useLiveSwap branch would, but week=0 keeps useLiveSwap off.
            // ASSUMPTION FLAGGED (carried over from the prior version): haven't reviewed
            // WeeklyRankingsExtensions.UpdateTeamRecord directly — confirm ZRoster is
            // excluded from whatever field list it maps, the same way RosterStrength/
            // PortalDelta already are, or this call nulls it back out before
            // RollingAverageService needs to read it.
            await _uow.TeamRecords.UpsertFromWeeklyRankingsAsync(year, token);

            // ── Pass 2: Trend/Seed/Pedigree, from PRIOR years' history only ────────────
            await _rollingAverageService.ComputeAndPersistAsync(year, 0, token);
            await _uow.SaveChangesAsync(token);

            // ── Pass 3: real week-0 values, now that TrendRating exists ────────────────
            var currentYearTeamRecords = (await _uow.TeamRecords.GetByYearAsync(year, token))
                .ToDictionary(r => r.TeamID);

            var historicalRecords = await _uow.TeamRecords.GetHistoricalAsync(
                fromYear: year - 5, toYearExclusive: year, token);
            var historyByTeam = historicalRecords
                .GroupBy(tr => tr.TeamID)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.Year).ToList());

            // PowerRating reference scale: most recently completed season's FBS-wide
            // PowerRating distribution. TrendRating is already z-scored against each of
            // its OWN source years internally (RollingAverageService.NormalizePowerRating)
            // — this is only about choosing a scale to render the [0,1] anchor back into
            // raw PowerRating-point terms, and last year's completed distribution is the
            // most representative one available before this year has any data of its own.
            var priorYearRecords = await _uow.TeamRecords.GetByYearAsync(year - 1, token);
            var priorYearTeamsDict = await _uow.Teams.GetByTeamIdsAsync(
                priorYearRecords.Select(r => r.TeamID).ToList(), token);
            var referenceLeagueStats = RollingAverageService.BuildLeagueYearStats(priorYearRecords, priorYearTeamsDict);
            
            referenceLeagueStats.TryGetValue((short)(year - 1), out var refStats);
            _logger.LogWarning(
                "refStats lookup for {Year}: found={Found}, keys=[{Keys}], mean={Mean}, stdDev={StdDev}, priorYearRecords.Count={Count}",
                year - 1, referenceLeagueStats.ContainsKey((short)(year - 1)),
                string.Join(",", referenceLeagueStats.Keys), refStats.Mean, refStats.StdDev,
                priorYearRecords.Count);

            // refStats defaults to (Mean: 0.0, StdDev: 0.0) if year-1 has no FBS
            // PowerRating data for some reason — RatingScaling.FromUnitScale treats
            // stdDev<=0 by returning `mean` (0.0) for every team: degenerate but safe
            // rather than a crash. Would be worth a warning log if that ever fires.

            var seedRows = new List<(int TeamId, decimal AvgScored, decimal AvgAllowed, decimal PowerRating, bool HasHistory)>();

            foreach (var t in fbsTeams)
            {
                if (!currentYearTeamRecords.TryGetValue(t.TeamId, out var record)) continue;

                historyByTeam.TryGetValue(t.TeamId, out var history);
                history ??= [];

                var scoredValues = history.Select(h => (double)h.AvgPointsScored).ToList();
                var allowedValues = history.Select(h => (double)h.AvgPointsAllowed).ToList();

                decimal avgScored = RollingAverageService.ApplyWeights(scoredValues, MetricsConfiguration.TrendWeights);
                decimal avgAllowed = RollingAverageService.ApplyWeights(allowedValues, MetricsConfiguration.TrendWeights);

                double trendUnit = record.TrendRating.HasValue ? (double)record.TrendRating.Value : 0.5;
                double powerRatingRaw = RatingScaling.FromUnitScale(trendUnit, refStats.Mean, refStats.StdDev);

                seedRows.Add((t.TeamId, avgScored, avgAllowed, (decimal)Math.Round(powerRatingRaw, 4), history.Count > 0));
            }

            // Backfill AvgPointsScored/AvgPointsAllowed for teams with ZERO qualifying
            // history (a program new to FBS, most likely) to the league-mean PPG/PAG
            // among teams that DO have history — not left at ApplyWeights([]) == 0m.
            // Raw 0 there is indistinguishable from a real in-season shutout once the
            // season starts, which is exactly the ambiguity that used to force
            // GamePredictionService.CalculatePrediction into a blanket 28.0 fallback
            // (Charlie: "if they played and got shut out, that's a different thing").
            // Backfilling to a real, data-driven league average here removes the need
            // for that fallback entirely — CalculatePrediction can now trust
            // AvgPointsScored/AvgPointsAllowed at face value, always, including a
            // legitimate 0 for a shutout.
            //
            // The 28.0m fallback below is a DIFFERENT, much narrower case than the one
            // it's replacing — it only fires if NO FBS team anywhere has any qualifying
            // history at all (i.e., the very first year this system is ever run), not
            // per-team. Worth keeping distinct in your head from the bug being fixed.
            var teamsWithHistory = seedRows.Where(r => r.HasHistory).ToList();
            decimal leagueMeanScored = teamsWithHistory.Count > 0
                ? Math.Round(teamsWithHistory.Average(r => r.AvgScored), 2) : 28.0m;
            decimal leagueMeanAllowed = teamsWithHistory.Count > 0
                ? Math.Round(teamsWithHistory.Average(r => r.AvgAllowed), 2) : 28.0m;

            seedRows = seedRows
                .Select(r => r.HasHistory
                    ? r
                    : (r.TeamId, leagueMeanScored, leagueMeanAllowed, r.PowerRating, r.HasHistory))
                .ToList();

            // Roster-composite adjustment: shift each team's weighted-history baseline
            // AvgPointsScored/AvgPointsAllowed by the year-over-year CHANGE in roster
            // talent (RosterCapacityService's Offensive/Defensive talent Z-score,
            // season vs season-1) — NOT the absolute roster Z-score itself. Research
            // puts 1.0 std dev of roster talent change at roughly 3.5–4.5 points/game
            // of offensive or defensive output; RosterZScoreToPointsFactor uses 4.0.
            // Defense is sign-flipped to match the existing defZ convention below
            // (allowing FEWER points than the delta implies is the improvement).
            // Teams with no usable prior-season roster Z (new to FBS, or the ZRoster
            // pipeline hasn't run for year-1) are absent from rosterZDeltas and are
            // left with their unadjusted weighted-history baseline rather than a
            // guessed delta.
            var rosterZDeltas = await _rosterCapacityService.GetRosterZScoreDeltasAsync(year, token);

            seedRows = seedRows
                .Select(r =>
                {
                    if (!rosterZDeltas.TryGetValue(r.TeamId, out var delta)) return r;

                    var adjustedScored = r.AvgScored + (decimal)(delta.OffensiveDelta * RosterZScoreToPointsFactor);
                    var adjustedAllowed = r.AvgAllowed - (decimal)(delta.DefensiveDelta * RosterZScoreToPointsFactor);

                    return (r.TeamId, adjustedScored, adjustedAllowed, r.PowerRating, r.HasHistory);
                })
                .ToList();

            // Ordinal ranks — based on the new week-0 PowerRating, NOT the (intentionally
            // undefined, zeroed) Ranking field. Ranking has no meaning before any games
            // are played (it's WinPct-based); PowerRating is the only week-0 quality
            // signal, so it's what OverallRank/TierRank sort on. Per Charlie.
            var orderedByPower = seedRows.OrderByDescending(r => r.PowerRating).ToList();
            var overallRankByTeam = orderedByPower
                .Select((r, i) => new { r.TeamId, Rank = i + 1 })
                .ToDictionary(x => x.TeamId, x => x.Rank);

            var teamsById = fbsTeams.ToDictionary(t => t.TeamId);
            var tierByTeamId = await _tierService.GetConfDataBatchAsync(
                teamsById.Keys, year, token);
            string TierFor(int teamId) =>
                tierByTeamId.TryGetValue(teamId, out var cd)
                    ? cd.Tier
                    : ConferenceTierService.GetTierStatic(null, teamsById[teamId].TeamName);

            var tierRankByTeam = new Dictionary<int, int>();
            foreach (var tierGroup in orderedByPower.GroupBy(r => TierFor(r.TeamId)))
            {
                int idx = 1;
                foreach (var r in tierGroup.OrderByDescending(x => x.PowerRating))
                    tierRankByTeam[r.TeamId] = idx++;
            }

            // ── Week-0 BaseSOS/SubSOS/CombinedSOS ───────────────────────────────────
            //
            // Same formula and same RatingCalculator.ResolveStrength fallback
            // WeeklyRankingsService.ComputeAndSaveAsync uses for every other week —
            // moved to RatingCalculator so both places can call it. At week 0 there
            // is no `prior` WeeklyRankings row for anyone (nothing's been computed
            // yet this year, and last year's values are deliberately not carried
            // forward — see the Ranking=0m note below), so every ResolveStrength
            // call here passes prior:null and collapses to tier 3 (raw SeedRating) —
            // that's the correct, intended preseason behavior, not a special case
            // that needs separate handling.
            //
            // Opponent set is the FULL season schedule (ResolvedGameResults.
            // GetByYearAsync — real result if played, else the locked Projection),
            // not just games "through week 0" (which would be empty and force
            // BaseSOS/SubSOS to 0 for everyone regardless of schedule strength).
            var seedByTeamId = currentYearTeamRecords.Values
                .Where(tr => tr.SeedRating.HasValue)
                .ToDictionary(tr => tr.TeamID, tr => tr.SeedRating!.Value);

            var fbsIds = fbsTeams.Select(t => t.TeamId).ToHashSet();
            var fullSeasonGames = await _uow.ResolvedGameResults.GetByYearAsync(year, token);

            var sosParticipants = fullSeasonGames
                .Where(g => fbsIds.Contains(g.HomeId ?? 0) || fbsIds.Contains(g.AwayId ?? 0))
                .SelectMany(g =>
                {
                    var homeId = g.HomeId ?? 0;
                    var awayId = g.AwayId ?? 0;
                    return new[]
                    {
                        new
                        {
                            TeamId           = homeId,
                            OpponentId       = awayId,
                            OpponentDivision = teamsById.TryGetValue(awayId, out var at)
                                ? at.Division : "fbs"
                        },
                        new
                        {
                            TeamId           = awayId,
                            OpponentId       = homeId,
                            OpponentDivision = teamsById.TryGetValue(homeId, out var ht)
                                ? ht.Division : "fbs"
                        }
                    };
                })
                .Select(p =>
                {
                    bool oppIsFcs = string.Equals(p.OpponentDivision, "fcs",
                                        StringComparison.OrdinalIgnoreCase);
                    decimal oppPregameStrength = oppIsFcs
                        ? 0m
                        : RatingCalculator.ResolveStrength(p.OpponentId, null, seedByTeamId);

                    return new
                    {
                        p.TeamId,
                        p.OpponentId,
                        DivWeight   = RatingCalculator.DivisionWeight(p.OpponentDivision),
                        OppStrength = (double)oppPregameStrength
                    };
                })
                .ToList();

            var baseSOS = sosParticipants
                .GroupBy(x => x.TeamId)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.DivWeight) > 0
                    ? Math.Round(
                        g.Sum(x => x.OppStrength * x.DivWeight) / g.Sum(x => x.DivWeight), 4)
                    : 0.0);

            var subSOS = sosParticipants
                .GroupBy(x => x.TeamId)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.DivWeight) > 0
                    ? Math.Round(
                        g.Sum(x => baseSOS.GetValueOrDefault(x.OpponentId, 0.0) * x.DivWeight) /
                        g.Sum(x => x.DivWeight), 4)
                    : 0.0);

            // Offensive/Defensive Z-scores — cross-sectional z-score of the new week-0
            // AvgPointsScored/AvgPointsAllowed across THIS week-0 cohort. Not carried
            // from last year's z-scores, which were scored against last year's FBS
            // population — a different, stale reference group.
            //
            // Sign convention matches WeeklyRankingsService's live per-game formula
            // (confirmed by reading it, not assumed):
            //   offZScore = (TeamPoints - expectedTeamScore) / stdDev    → higher scoring = positive
            //   defZScore = (expectedOppScore - OpponentPoints) / stdDev → allowing FEWER points = positive
            // Week-0 cross-sectional analog: Offensive is a plain z-score of
            // AvgPointsScored. Defensive is the z-score of AvgPointsAllowed, SIGN-FLIPPED
            // — allowing more points than league average is bad, not good.
            double scoredMean = seedRows.Count > 0 ? seedRows.Average(r => (double)r.AvgScored) : 0.0;
            double scoredStdDev = seedRows.Count > 1
                ? Math.Sqrt(seedRows.Average(r => Math.Pow((double)r.AvgScored - scoredMean, 2))) : 0.0;
            double allowedMean = seedRows.Count > 0 ? seedRows.Average(r => (double)r.AvgAllowed) : 0.0;
            double allowedStdDev = seedRows.Count > 1
                ? Math.Sqrt(seedRows.Average(r => Math.Pow((double)r.AvgAllowed - allowedMean, 2))) : 0.0;

            var offZByTeam = new Dictionary<int, decimal>();
            var defZByTeam = new Dictionary<int, decimal>();
            foreach (var r in seedRows)
            {
                double offZ = scoredStdDev > 0 ? ((double)r.AvgScored - scoredMean) / scoredStdDev : 0.0;
                double defZ = allowedStdDev > 0 ? (allowedMean - (double)r.AvgAllowed) / allowedStdDev : 0.0;

                // ASSUMPTION FLAGGED: applying RatingCalculator.DampenZScore here for
                // consistency with every other z-score WeeklyRankingsService writes —
                // haven't reviewed its implementation directly. If it assumes something
                // specific to per-game inputs that doesn't transfer to a cross-sectional
                // preseason z-score, flag it and I'll adjust.
                offZByTeam[r.TeamId] = (decimal)RatingCalculator.DampenZScore(offZ);
                defZByTeam[r.TeamId] = (decimal)RatingCalculator.DampenZScore(defZ);
            }

            var offensiveRankByTeam = seedRows
                .OrderByDescending(r => offZByTeam[r.TeamId])
                .Select((r, i) => new { r.TeamId, Rank = i + 1 })
                .ToDictionary(x => x.TeamId, x => x.Rank);

            var defensiveRankByTeam = seedRows
                .OrderByDescending(r => defZByTeam[r.TeamId])
                .Select((r, i) => new { r.TeamId, Rank = i + 1 })
                .ToDictionary(x => x.TeamId, x => x.Rank);

            // ── Write it all back ────────────────────────────────────────────────────
            var weekZeroRows = (await _uow.WeeklyRankings.GetByYearAndWeekAsync(year, 0, token))
                .ToDictionary(wr => wr.TeamID);

            int seeded = 0;
            foreach (var r in seedRows)
            {
                if (weekZeroRows.TryGetValue(r.TeamId, out var wr))
                {
                    wr.PowerRating = r.PowerRating;
                    wr.Ranking = 0m; // intentionally undefined pre-season, per Charlie
                    wr.BaseSOS = (decimal)baseSOS.GetValueOrDefault(r.TeamId, 0.0);
                    wr.SubSOS = (decimal)subSOS.GetValueOrDefault(r.TeamId, 0.0);
                    wr.CombinedSOS = (decimal)Math.Round(
                        (2 * (double)wr.BaseSOS + 3 * (double)wr.SubSOS) / 5.0, 4);
                    wr.OverallRank = overallRankByTeam.GetValueOrDefault(r.TeamId, 0);
                    wr.TierRank = tierRankByTeam.GetValueOrDefault(r.TeamId, 0);
                    wr.AvgPointsScored = r.AvgScored;
                    wr.AvgPointsAllowed = r.AvgAllowed;
                    wr.OffensiveZScore = offZByTeam.GetValueOrDefault(r.TeamId, 0m);
                    wr.DefensiveZScore = defZByTeam.GetValueOrDefault(r.TeamId, 0m);
                    wr.OffensiveRank = offensiveRankByTeam.GetValueOrDefault(r.TeamId, 0);
                    wr.DefensiveRank = defensiveRankByTeam.GetValueOrDefault(r.TeamId, 0);
                }

                if (currentYearTeamRecords.TryGetValue(r.TeamId, out var tr))
                {
                    // SeedRating/TrendRating/PedigreeRating/ZRoster deliberately
                    // untouched — already correctly set by RollingAverageService
                    // (Pass 2) and the separate roster-capacity pipeline respectively.
                    // TeamRecord.OffensiveZScore/DefensiveZScore ARE written here —
                    // TeamRecord is a season rollup of WeeklyRanking, so it must carry
                    // the same scoring-based Week-0 offense/defense Z as wr above.
                    tr.PowerRating = r.PowerRating;
                    tr.Ranking = 0m;
                    tr.BaseSOS = (decimal)baseSOS.GetValueOrDefault(r.TeamId, 0.0);
                    tr.SubSOS = (decimal)subSOS.GetValueOrDefault(r.TeamId, 0.0);
                    tr.CombinedSOS = (decimal)Math.Round(
                        (2 * (double)tr.BaseSOS + 3 * (double)tr.SubSOS) / 5.0, 4);
                    tr.AvgPointsScored = r.AvgScored;
                    tr.AvgPointsAllowed = r.AvgAllowed;
                    tr.OffensiveZScore = offZByTeam.GetValueOrDefault(r.TeamId, 0m);
                    tr.DefensiveZScore = defZByTeam.GetValueOrDefault(r.TeamId, 0m);
                    tr.OffensiveRank = offensiveRankByTeam.GetValueOrDefault(r.TeamId, 0);
                    tr.DefensiveRank = defensiveRankByTeam.GetValueOrDefault(r.TeamId, 0);
                }

                seeded++;
            }

            await _uow.SaveChangesAsync(token);

            var zRosterAppliedCount = currentYearTeamRecords.Values.Count(tr => tr.ZRoster.HasValue);

            _logger.LogInformation(
                "Season {Year} initialized — {Count} teams seeded from weighted 5-year history " +
                "(TrendWeights) and TrendRating-derived PowerRating, not copied from {PriorYear}'s " +
                "final snapshot. {ZRosterCount} teams had ZRoster applied to Seed.",
                year, seeded, year - 1, zRosterAppliedCount);

            return new
            {
                message = $"Season {year} initialized successfully.",
                year,
                week = 0,
                teamsSeeded = seeded,
                zRosterApplied = zRosterAppliedCount
            };
        }
        /// <summary>
        /// Backfills week 0 snapshots for all years that have WeeklyRankings data
        /// but no week 0 entry. Safe to run multiple times — skips already-initialized years.
        ///
        /// Run once after the initial data load, before backfillWeeklyRankings.
        /// </summary>
        /// <summary>
        /// Streaming version — yields one ProgressUpdate per year as it's initialized.
        ///
        /// NOTE: the previous version of this method called BackfillWeeklyRankingsAsync
        /// per year internally, marked "//<<=== Add this." — removed here. The console
        /// now runs Initialize Seasons and Weekly Rankings as two explicit, sequential
        /// steps, so the internal call was doubling work: every year that needed
        /// initializing would get its WeeklyRankings computed twice (once here, once
        /// again when Weekly Rankings runs next). If that internal call was there for
        /// a reason other than convenience, flag it and I'll put it back.
        /// </summary>
        public async IAsyncEnumerable<ProgressUpdate> BackfillInitializeSeasonsStreamAsync(
            int? startYear, [EnumeratorCancellation] CancellationToken token = default)
        {
            var allSnapshots = await _uow.WeeklyRankings.GetDistinctYearWeeksAsync(token);

            var yearsWithData = allSnapshots
                .Select(s => (int)s.Year)
                .Distinct()
                .OrderBy(y => y)
                .ToList();

            if (startYear.HasValue)
                yearsWithData = yearsWithData.Where(y => y >= startYear.Value).ToList();

            var yearsWithWeek0 = allSnapshots
                .Where(s => s.Week == 0)
                .Select(s => (int)s.Year)
                .ToHashSet();

            // Only process years missing week 0 that have a prior year to seed from.
            var yearsToInitialize = yearsWithData
                .Where(y => !yearsWithWeek0.Contains(y) && yearsWithData.Contains(y - 1))
                .ToList();

            if (!yearsToInitialize.Any())
            {
                yield return new ProgressUpdate("summary", true, "All seasons already initialized.");
                yield break;
            }

            foreach (var year in yearsToInitialize)
            {
                token.ThrowIfCancellationRequested();

                bool success; string message;
                try
                {
                    await InitializeSeasonAsync(year, token);
                    success = true;
                    message = "season initialized";
                }
                catch (Exception ex)
                {
                    success = false;
                    message = ex.Message;
                }

                yield return new ProgressUpdate(year.ToString(), success, message);
            }
        }

        // ── Weekly Rankings ───────────────────────────────────────────────────────

        /// <summary>
        /// Backfills WeeklyRankings for all year/week combinations from startYear onward.
        /// Includes both historical years (played games) and future years (unplayed games).
        /// Rolling averages run once per year for performance rather than once per week.
        /// </summary>
        /// <summary>
        /// Streaming version — yields one ProgressUpdate per year. Restructured from
        /// the original's "run rolling averages when the year rolls over, plus once
        /// more for the final year" pattern to an explicit per-year group: same end
        /// result (rolling averages computed once per year, after that year's weeks
        /// are all done), without needing the separate post-loop call for the last
        /// year. SOS/Power Ratings/Rankings are computed inside
        /// _weeklyRankingsService.ComputeAndSaveAsync per week, same as before —
        /// that's why those aren't exposed as separate console ops anymore.
        /// </summary>
        public async IAsyncEnumerable<ProgressUpdate> BackfillWeeklyRankingsStreamAsync(
            int? startYear, [EnumeratorCancellation] CancellationToken token = default)
        {
            var fromYear = startYear ?? 1960;

            var allGames = await _uow.Games.GetGamesSinceYearAsync(fromYear, token);

            var yearWeeks = allGames
                .Select(g => new { g.Year, g.Week })
                .Distinct()
                .OrderBy(g => g.Year).ThenBy(g => g.Week)
                .ToList();

            if (!yearWeeks.Any())
            {
                yield return new ProgressUpdate("summary", false, "No games found matching the criteria.");
                yield break;
            }

            foreach (var yearGroup in yearWeeks.GroupBy(yw => yw.Year))
            {
                token.ThrowIfCancellationRequested();

                bool success = true;
                var weeksProcessed = 0;
                string message;

                try
                {
                    foreach (var yw in yearGroup)
                    {
                        // Skip rolling averages per-week — run once per year below.
                        await _weeklyRankingsService.ComputeAndSaveAsync(
                            yw.Year, yw.Week, token, computeRollingAverages: false);
                        weeksProcessed++;
                    }

                    await _rollingAverageService.ComputeAndPersistAsync(yearGroup.Key, null, token);
                    message = $"{weeksProcessed} week(s) computed";
                }
                catch (Exception ex)
                {
                    success = false;
                    message = ex.Message;
                }

                yield return new ProgressUpdate(yearGroup.Key.ToString(), success, message);
            }
        }

        public async Task<ComputeWeeklyResult> ComputeWeeklyAsync(
            int? year, int? week, bool backfill, CancellationToken token)
        {
            var targetYear = year ?? DateTime.Now.Year;

            if (backfill)
            {
                await _weeklyRankingsService.BackfillYearAsync(targetYear, token);
                return new ComputeWeeklyResult($"Backfilled all weeks for {targetYear}.", targetYear, null);
            }

            if (!week.HasValue)
                throw new ArgumentException("Provide week=N or backfill=true.");

            await _weeklyRankingsService.ComputeAndSaveAsync(targetYear, week.Value, token);
            return new ComputeWeeklyResult(
                $"Computed weekly rankings for {targetYear} week {week.Value}.", targetYear, week.Value);
        }

        // DIAGNOSTIC — read-only. Delegates to WeeklyRankingsService, which owns all
        // the context (priorByTeamId, seedByTeamId, avgScoreDifferentials, etc.)
        // this needs — see its own remarks for why this lives there and not here.
        // Not AnalyzeTeamGamesAsync above — that one predates the current z-score
        // pipeline (no ResolveStrength/ExpandStrength/QualityMod/DivWeight) and
        // won't match production numbers; use this one instead.
        public Task<WeeklyRankingsService.TeamGameZScoreAnalysis> AnalyzeTeamGameZScoresAsync(
            int teamId, int year, int week, CancellationToken token = default)
            => _weeklyRankingsService.AnalyzeTeamGameZScoresAsync(teamId, year, week, token);

        // DIAGNOSTIC — read-only. Delegates to GamePredictionService, which already
        // has both dependencies (_avgScoreDifferentialService, _blendedRating) this
        // needs — see its own remarks for why this lives there.
        public Task<GamePredictionService.PredictionMathDetail> AnalyzePredictionMathAsync(
            int year, string teamName, string opponentName, char location, int week,
            double? hfaOverride = null, CancellationToken token = default)
            => _predictionService.AnalyzePredictionMathAsync(
                year, teamName, opponentName, location, week, hfaOverride, token);

        public Task<List<GamePredictionService.SeasonPredictionMathGame>> AnalyzeSeasonPredictionMathAsync(
            int year, int teamId, double? hfaOverride = null, CancellationToken token = default)
            => _predictionService.AnalyzeSeasonPredictionMathAsync(year, teamId, hfaOverride, token);


        // BackfillProjectionsStreamAsync removed — the old multi-snapshot-per-game
        // Projections design (one row per (GameId, snapshotWeek) pass) was replaced
        // by Option C in WeeklyRankingsService.ComputeAndSaveAsync, which writes at
        // most one locked Projection row per GameId, keyed at the game's own native
        // week. BackfillWeeklyRankingsStreamAsync now fully covers Projections
        // population as a side effect of its per-week ComputeAndSaveAsync calls.
        // Confirmed never run against a season since the Option C rewrite — no
        // stale data to clean up.
    }
}
