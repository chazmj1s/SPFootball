using Microsoft.Extensions.Caching.Memory;
using SaturdayPulse.Contracts;
using SaturdayPulse.Contracts.Requests;
using SaturdayPulse.Contracts.Responses;
using SaturdayPulse.Interfaces;
using SaturdayPulse.Models;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using SaturdayPulse.Utilities;
using SQLitePCL;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Timers;

namespace SaturdayPulse.Services
{
    /// <summary>
    /// ProductionGameDataService — Teams and Rankings.
    /// Team-record queries, rolling averages, season arcs, and Power Rankings.
    /// </summary>
    public partial class ProductionGameDataService
    {

        public async Task<List<int>> GetTeamAvailableYearsAsync(
            int teamId, CancellationToken token = default)
        {
            var years = await _uow.WeeklyRankings.GetDistinctYearWeeksAsync(token);
            return years
                .Where(yw => yw.Year >= 1965)
                .Select(yw => yw.Year)
                .Distinct()
                .OrderByDescending(y => y)
                .ToList();
        }


        // ── Queries ───────────────────────────────────────────────────────────────

        public async Task<TeamRecordsQueryResult> QueryTeamRecordsAsync(
            int? wins, int? losses, int? minWins, int? maxWins,
            int? startYear, int? endYear,
            decimal? minPowerRating, decimal? maxPowerRating,
            int limit, CancellationToken token = default)
        {
            var results = await _uow.TeamRecords.QueryAsync(
                wins, losses, minWins, maxWins, startYear, endYear,
                minPowerRating, maxPowerRating, limit, token);

            var mapped = results.Select(tr => (object)new
            {
                tr.Year,
                TeamName          = tr.Teams!.TeamName,
                Record            = $"{tr.Wins}-{tr.Losses}",
                tr.Wins, tr.Losses, tr.PointsFor, tr.PointsAgainst,
                PointDifferential = tr.PointsFor - tr.PointsAgainst,
                tr.BaseSOS, tr.SubSOS, tr.CombinedSOS, tr.PowerRating
            }).ToList();

            var filters = (object)new { wins, losses, minWins, maxWins, startYear, endYear, minPowerRating, maxPowerRating, limit };
            return new TeamRecordsQueryResult(mapped.Count, filters, mapped);
        }


        // ── Rolling Averages ─────────────────────────────────────────────────────

        public async Task<RollingAveragesResult> GetRollingAveragesAsync(int? year, CancellationToken token = default)
        {
            var targetYear = year ?? DateTime.Now.Year;

            var currentRecords = await _uow.TeamRecords.GetFbsByYearAsync(targetYear, token);
            currentRecords = currentRecords
                .Where(r => r.TrendRating != null || r.PedigreeRating != null)
                .ToList();

            if (!currentRecords.Any())
                throw new KeyNotFoundException($"No rolling average data found for {targetYear}.");

            var historicalRecords = await _uow.TeamRecords.GetHistoricalAsync(targetYear - 10, targetYear, token);
            var historyByTeam     = historicalRecords
                .GroupBy(tr => tr.TeamID)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.Year).ToList());

            // League-wide PowerRating distribution, needed to normalize PowerRating
            // onto a comparable [0,1] scale the same way RollingAverageService does
            // internally. FBS membership uses the current year's team list as a proxy
            // for every historical year — same known simplification documented in
            // RollingAverageService.BuildLeagueYearStats itself.
            var teamsDictForStats = await _uow.Teams.GetByTeamIdsAsync(
                currentRecords.Select(r => r.TeamID).ToList(), token);
            var leagueStatsByYear = RollingAverageService.BuildLeagueYearStats(
                historicalRecords.Concat(currentRecords), teamsDictForStats);

            var results = currentRecords.Select(r =>
            {
                historyByTeam.TryGetValue(r.TeamID, out var history);
                history ??= [];
                var avg = _rollingAverageService.Compute(
                    r, history, useLiveSwap: false, week: null, leagueStatsByYear);
                return (object)new
                {
                    teamId          = r.TeamID,
                    teamName        = r.Teams?.TeamName,
                    conference      = r.Teams?.Conference?.Abbreviation,
                    seedRating      = avg.SeedRating,
                    trendRating     = avg.TrendRating,
                    trendHistory    = avg.TrendHistory,
                    pedigreeRating  = avg.PedigreeRating,
                    pedigreeHistory = avg.PedigreeHistory
                };
            })
            .OrderByDescending(r => ((dynamic)r).trendRating)
            .ToList();

            return new RollingAveragesResult(targetYear, results.Count, results);
        }


        public async Task<TeamRollingAveragesResult> GetTeamRollingAveragesAsync(
            int teamId, int? startYear, CancellationToken token = default)
        {
            var team = await _uow.Teams.GetByTeamIdAsync(teamId, token)
                       ?? throw new KeyNotFoundException($"Team {teamId} not found.");

            var allRecords = await _uow.TeamRecords.GetByTeamAllYearsAsync(teamId, token);

            if (!allRecords.Any())
                throw new KeyNotFoundException($"No records found for team {teamId}.");

            var history       = allRecords.OrderByDescending(r => r.Year).ToList();
            var targetRecords = startYear.HasValue
                ? allRecords.Where(r => r.Year >= startYear.Value).ToList()
                : allRecords;

            // This endpoint only ever loaded ONE team's records — no league-wide data
            // was in scope before. Normalizing PowerRating correctly requires it, so
            // this is a genuinely new query added here (not just a signature fix).
            // Covers every year that could appear as a "current" or historical year
            // across all target records being computed below.
            var minYear = targetRecords.Min(r => r.Year) - 10;
            var maxYearExclusive = targetRecords.Max(r => r.Year) + 1;
            var leagueRecords = await _uow.TeamRecords.GetHistoricalAsync(minYear, maxYearExclusive, token);
            var teamsDictForStats = await _uow.Teams.GetDictionaryByTeamIdAsync(token);
            var leagueStatsByYear = RollingAverageService.BuildLeagueYearStats(leagueRecords, teamsDictForStats);

            var results = targetRecords.Select(r =>
            {
                var priorRecords = history.Where(h => h.Year < r.Year).Take(10).ToList();
                var avg          = _rollingAverageService.Compute(
                    r, priorRecords, useLiveSwap: false, week: null, leagueStatsByYear);
                return (object)new
                {
                    year            = (int)r.Year,
                    wins            = (int)r.Wins,
                    losses          = (int)r.Losses,
                    seedRating      = avg.SeedRating,
                    trendRating     = avg.TrendRating,
                    trendHistory    = avg.TrendHistory,
                    pedigreeRating  = avg.PedigreeRating,
                    pedigreeHistory = avg.PedigreeHistory
                };
            }).ToList();

            return new TeamRollingAveragesResult(team.TeamId, team.TeamName, team.Conference?.Abbreviation, results);
        }


        // ── Team History ─────────────────────────────────────────────────────────

        public async Task<TeamHistoryResult> GetTeamHistoryAsync(
            int teamId, int years, CancellationToken token = default)
        {
            var team = await _uow.Teams.GetByTeamIdAsync(teamId, token)
                       ?? throw new KeyNotFoundException($"Team {teamId} not found.");

            var confLookup = await _uow.Conferences.GetDictionaryAsync(token);
            confLookup.TryGetValue(team.ConferenceId ?? 0, out var conf);
            var confAbbr = conf?.Abbreviation ?? string.Empty;

            var cutoffYear = (short)(DateTime.Now.Year - years);
            var records    = await _uow.TeamRecords.GetByTeamAllYearsAsync(teamId, token);
            records = records.Where(r => r.Year >= cutoffYear).ToList();

            var allYears    = records.Select(r => r.Year).Distinct().ToList();
            var ranksByYear = new Dictionary<short, int>();
            var tierByYear  = new Dictionary<short, string>();

            foreach (var yr in allYears)
            {
                var allRanked = await _uow.TeamRecords.GetRankedByYearAsync(yr, token);
                var idx       = allRanked.FindIndex(tr => tr.TeamID == teamId);
                if (idx >= 0) ranksByYear[yr] = idx + 1;

                // Year-specific tier, not the team's current conference — a team
                // that changed conferences (Nebraska Big 12→Big Ten, Texas SWC→
                // Big 12) must show the tier it actually held in yr, not today's.
                // Uses GetConfDataBatchAsync directly (not GetConfDataAsync, whose
                // own internal fallback is a hardcoded "Other") so a missing year
                // falls back through GetTierStatic like every other call site.
                var confDataForYear = await _tierService.GetConfDataBatchAsync(
                    new[] { teamId }, yr, token);
                tierByYear[yr] = confDataForYear.TryGetValue(teamId, out var cd)
                    ? cd.Tier
                    : ConferenceTierService.GetTierStatic(null, team.TeamName);
            }

            var history = records.Select(r => (object)new
            {
                Year        = (int)r.Year,
                r.Wins,
                r.Losses,
                Record      = $"{r.Wins}-{r.Losses}",
                PowerRating = r.Ranking,
                BaseSOS     = r.BaseSOS,
                CombinedSOS = r.CombinedSOS,
                OverallRank = ranksByYear.GetValueOrDefault(r.Year, 0),
                Tier        = tierByYear.GetValueOrDefault(r.Year, "Other")
            }).ToList();

            return new TeamHistoryResult(teamId, team.TeamName, team.Abbreviation ?? team.TeamName, confAbbr, history);
        }


        // ── Teams ────────────────────────────────────────────────────────────────

        /// <summary>
        /// V2: reads from Teams + Conferences tables (CFBD-sourced).
        /// Legacy equivalent: GetTeamsAsync() which reads from Team table.
        ///
        /// Key differences from legacy:
        ///   - Conference name/abbr resolved via ConferenceId FK, not embedded in Team row
        ///   - ShortName not available from CFBD /teams endpoint; falls back to Abbreviation
        ///   - TeamId (int) replaces TeamID (int) — same value, different property name
        /// </summary>
        public async Task<TeamsResult> GetTeamsV2Async(CancellationToken token = default)
        {
            var teams      = await _uow.Teams.GetAllAsync(token);
            var confLookup = await _uow.Conferences.GetDictionaryAsync(token);

            var result = teams
                .Where(t => string.Equals(t.Division, "fbs", StringComparison.OrdinalIgnoreCase))
                .Select(t =>
                {
                    confLookup.TryGetValue(t.ConferenceId ?? -1, out var conf);

                    return (object)new
                    {
                        TeamID         = t.TeamId,
                        t.TeamName,
                        ShortName      = t.Abbreviation ?? t.TeamName,
                        Conference     = conf?.Name ?? string.Empty,
                        ConferenceAbbr = conf?.Abbreviation ?? string.Empty,
                        Division       = t.Division?.ToUpperInvariant(),
                        // "Current teams" listing has no year of its own — DateTime.Now.Year
                        // matches the same "current" convention GetTeamHistoryAsync uses for
                        // cutoffYear. ClassifyConference is synchronous, so no batching needed.
                        Tier           = _tierService.ClassifyConference(
                                             t.ConferenceId ?? 0, conf?.Classification, DateTime.Now.Year)
                    };
                })
                .ToList();

            return new TeamsResult(result);
        }


        /// <summary>
        /// V2: reads team metadata from Teams + Conferences instead of Team.
        /// WeeklyRankings and TeamRecords are shared tables — unchanged.
        /// </summary>
        public async Task<PowerRankingsResult> GetPowerRankingsV2Async(
            int? year, int? throughWeek, CancellationToken token = default)
        {
            try
            {
                var targetYear = year ?? DateTime.Now.Year;
                var Teams = await _uow.Teams.GetDictionaryByTeamIdAsync(token);

                var confDataByTeamId = await _tierService.GetConfDataBatchAsync(
                    Teams.Keys, targetYear, token);

                ConferenceTierService.ConferenceData ConfData(int? teamId, string? teamName = null)
                {
                    if (teamId.HasValue && confDataByTeamId.TryGetValue(teamId.Value, out var cd))
                        return cd;
                    var staticTier = ConferenceTierService.GetTierStatic(null, teamName);
                    return new ConferenceTierService.ConferenceData(string.Empty, string.Empty, staticTier);
                }

                if (throughWeek.HasValue)
                {
                    var lookupWeek = Math.Max(0, throughWeek.Value);
                    var weekly = await _uow.WeeklyRankings.GetByYearAndWeekAsync(
                        targetYear, lookupWeek, token);

                    if (!weekly.Any())
                        throw new KeyNotFoundException(
                            $"No weekly rankings found for year {targetYear} week {throughWeek}.");

                    var currentRecords = await _uow.TeamRecords.GetByYearAsync(targetYear, token);
                    var currentRecordLookup = currentRecords.ToDictionary(r => r.TeamID);

                    var rosterRankByTeam = currentRecords
                        .Where(r => r.ZRoster.HasValue)
                        .OrderByDescending(r => r.ZRoster!.Value)
                        .Select((r, i) => new { r.TeamID, Rank = i + 1 })
                        .ToDictionary(x => x.TeamID, x => x.Rank);

                    var historicalRecords = await _uow.TeamRecords.GetHistoricalAsync(
                        targetYear - 10, targetYear, token);
                    var historyByTeam = historicalRecords
                        .GroupBy(tr => tr.TeamID)
                        .ToDictionary(g => g.Key, g => g.OrderBy(r => r.Year).ToList());

                    var projGames = await _uow.Games.GetByYearAsync(targetYear, token);
                    var projProjections = await _projectionCache.GetAllProjections(targetYear, token);
                    var projRecordByTeam = BuildProjectedRecordRollup(projGames, projProjections, throughWeek);
                    var actualRecordByTeam = BuildActualRecordRollup(projGames, throughWeek);

                    var sample = currentRecords.FirstOrDefault();
                    Debug.WriteLine($"Sample TrendRating: {sample?.TrendRating}, Pedigree: {sample?.PedigreeRating}, Seed: {sample?.SeedRating}");

                    // ── Composite sort key — 2026-08-22 (Charlie) ────────────────────────
                    // Rankings-page default sort: composite (actual + projected) win%,
                    // tiebreak CombinedSOS, then RosterRank. Replaces the prior
                    // wr.OverallRank ordering, which carried WeeklyRankingsService's own
                    // Ranking-based rank straight through — a different, separately
                    // circular computation (see WeeklyRankingsService Step 10) that this
                    // change deliberately does NOT touch. Wins/Losses/RosterRank now need
                    // resolving before the sort, not just inside the final projection.
                    var eligible = weekly
                        .Where(wr => wr.Ranking.HasValue)
                        .Where(wr => currentRecordLookup.ContainsKey(wr.TeamID))
                        .Select(wr =>
                        {
                            projRecordByTeam.TryGetValue(wr.TeamID, out var projWL);
                            actualRecordByTeam.TryGetValue(wr.TeamID, out var actualWL);
                            var rosterRank = rosterRankByTeam.TryGetValue(wr.TeamID, out var rr) ? rr : (int?)null;
                            Teams.TryGetValue(wr.TeamID, out var t0);
                            var conf = ConfData(wr.TeamID, t0?.TeamName);

                            var compositeWins = actualWL.Wins + projWL.Wins;
                            var compositeLosses = actualWL.Losses + projWL.Losses;
                            var compositeTotal = compositeWins + compositeLosses;

                            return new
                            {
                                Weekly = wr,
                                ActualWL = actualWL,
                                ProjWL = projWL,
                                RosterRank = rosterRank,
                                Conf = conf,
                                CompositeWinPct = compositeTotal > 0
                                    ? (double)compositeWins / compositeTotal
                                    : 0.0
                            };
                        })
                        .OrderByDescending(x => x.Weekly.Ranking)
                        .ThenByDescending(x => (double?)x.Weekly.CombinedSOS ?? double.MinValue)
                        .ThenBy(x => x.RosterRank ?? int.MaxValue)
                        .ToList();

                    // TierRank recomputed from the same composite order, grouped by tier
                    // — same pattern the no-throughWeek branch below already used for its
                    // own TierRank; group enumeration preserves eligible's sort order, so
                    // no redundant OrderBy needed inside the loop.
                    var tierRankLookup = new Dictionary<int, int>();
                    foreach (var tierGroup in eligible.GroupBy(x => x.Conf.Tier))
                    {
                        var tieredTeams = tierGroup
                            .Select((x, i) => new { x.Weekly.TeamID, TierRank = i + 1 })
                            .ToList();
                        foreach (var team in tieredTeams)
                            tierRankLookup[team.TeamID] = team.TierRank;
                    }

                    var result = eligible
                        .Select((x, i) =>
                        {
                            var wr = x.Weekly;
                            Teams.TryGetValue(wr.TeamID, out var t);
                            currentRecordLookup.TryGetValue(wr.TeamID, out var currentRecord);
                            historyByTeam.TryGetValue(wr.TeamID, out var history);
                            history ??= [];

                            return new PowerRankingRowResponse
                            {
                                TeamID = wr.TeamID,
                                TeamName = t?.TeamName,
                                Conference = x.Conf.Name,
                                ConferenceAbbr = x.Conf.Abbreviation,
                                Division = t?.Division,
                                Tier = x.Conf.Tier,
                                OverallRank = i + 1,
                                TierRank = tierRankLookup[wr.TeamID],
                                Ranking = (double?)wr.Ranking,
                                PowerRating = (double?)wr.PowerRating,
                                Year = (int)wr.Year,
                                Wins = x.ActualWL.Wins,
                                Losses = x.ActualWL.Losses,
                                ProjectedWins = x.ActualWL.Wins + x.ProjWL.Wins,
                                ProjectedLosses = x.ActualWL.Losses + x.ProjWL.Losses,
                                BaseSOS = (double?)wr.BaseSOS,
                                CombinedSOS = (double?)wr.CombinedSOS,
                                AvgPointsScored = (double?)wr.AvgPointsScored,
                                AvgPointsAllowed = (double?)wr.AvgPointsAllowed,
                                OffensiveZScore = (double?)wr.OffensiveZScore,
                                DefensiveZScore = (double?)wr.DefensiveZScore,
                                OffensiveRank = wr.OffensiveRank,
                                DefensiveRank = wr.DefensiveRank,
                                RosterRank = x.RosterRank,
                                TrendRating = (double?)(currentRecord?.TrendRating),
                                PedigreeRating = (double?)currentRecord?.PedigreeRating,
                                SeedRating = (double?)currentRecord?.SeedRating,
                                TrendHistory = history.Select(h => (double)(h.TrendRating ?? 0m)).ToList(),
                                PedigreeHistory = history.Select(h => (double)(h.PedigreeRating ?? 0m)).ToList()
                            };
                        }).ToList();

                    var sample2 = result.FirstOrDefault();
                    Debug.WriteLine($"Response TrendRating: {sample2?.TrendRating}, Pedigree: {sample2?.PedigreeRating}, Seed: {sample2?.SeedRating}");

                    return new PowerRankingsResult(true, result);
                }
                else
                {
                    var teamRecords = await _uow.TeamRecords.GetByYearWithTeamsAsync(targetYear, token);
                    var ranked = teamRecords.Where(tr => tr.Ranking.HasValue).ToList();

                    var rosterRankByTeam = teamRecords
                        .Where(r => r.ZRoster.HasValue)
                        .OrderByDescending(r => r.ZRoster!.Value)
                        .Select((r, i) => new { r.TeamID, Rank = i + 1 })
                        .ToDictionary(x => x.TeamID, x => x.Rank);

                    var projGames = await _uow.Games.GetByYearAsync(targetYear, token);
                    var projProjections = await _projectionCache.GetAllProjections(targetYear, token);
                    var projRecordByTeam = BuildProjectedRecordRollup(projGames, projProjections, null);
                    var actualRecordByTeam = BuildActualRecordRollup(projGames, null);

                    // Same composite key as the throughWeek branch above — resolved here
                    // too, since ordering now needs it before OverallRank is assigned.
                    var withTiers = ranked
                        .Select(tr =>
                        {
                            Teams.TryGetValue(tr.TeamID, out var t);
                            var conf = ConfData(tr.TeamID, t?.TeamName);
                            projRecordByTeam.TryGetValue(tr.TeamID, out var projWL);
                            actualRecordByTeam.TryGetValue(tr.TeamID, out var actualWL);
                            var rosterRank = rosterRankByTeam.TryGetValue(tr.TeamID, out var rr) ? rr : (int?)null;

                            var compositeWins = actualWL.Wins + projWL.Wins;
                            var compositeLosses = actualWL.Losses + projWL.Losses;
                            var compositeTotal = compositeWins + compositeLosses;

                            return new
                            {
                                TeamRecord = tr,
                                Team = t,
                                Conf = conf,
                                ActualWL = actualWL,
                                ProjWL = projWL,
                                RosterRank = rosterRank,
                                CompositeWinPct = compositeTotal > 0
                                    ? (double)compositeWins / compositeTotal
                                    : 0.0
                            };
                        })
                        .OrderByDescending(t => t.CompositeWinPct)
                        .ThenByDescending(t => (double?)t.TeamRecord.CombinedSOS ?? double.MinValue)
                        .ThenBy(t => t.RosterRank ?? int.MaxValue)
                        .ToList();

                    var withOverallRank = withTiers
                        .Select((t, i) => new
                        {
                            t.TeamRecord,
                            t.Team,
                            t.Conf,
                            t.ActualWL,
                            t.ProjWL,
                            t.RosterRank,
                            OverallRank = i + 1
                        })
                        .ToList();

                    var tierRankLookup = new Dictionary<int, int>();
                    foreach (var tierGroup in withOverallRank.GroupBy(t => t.Conf.Tier))
                    {
                        var tieredTeams = tierGroup
                            .Select((t, i) => new { t.TeamRecord.TeamID, TierRank = i + 1 })
                            .ToList();
                        foreach (var team in tieredTeams)
                            tierRankLookup[team.TeamID] = team.TierRank;
                    }

                    var rankings = withOverallRank
                        .Select(t => new PowerRankingRowResponse
                        {
                            TeamID = t.TeamRecord.TeamID,
                            TeamName = t.Team?.TeamName ?? t.TeamRecord.Teams?.TeamName,
                            Conference = t.Conf.Name,
                            ConferenceAbbr = t.Conf.Abbreviation,
                            Division = t.Team?.Division,
                            Tier = t.Conf.Tier,
                            OverallRank = t.OverallRank,
                            TierRank = tierRankLookup[t.TeamRecord.TeamID],
                            Ranking = (double?)t.TeamRecord.Ranking,
                            Year = t.TeamRecord.Year,
                            Wins = t.ActualWL.Wins,
                            Losses = t.ActualWL.Losses,
                            BaseSOS = (double?)t.TeamRecord.BaseSOS,
                            CombinedSOS = (double?)t.TeamRecord.CombinedSOS,
                            RosterRank = t.RosterRank,
                            ProjectedWins = t.ActualWL.Wins + t.ProjWL.Wins,
                            ProjectedLosses = t.ActualWL.Losses + t.ProjWL.Losses
                        }).ToList();

                    return new PowerRankingsResult(false, rankings);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception: {ex.Message}");
            }

            return new PowerRankingsResult(false, new List<PowerRankingRowResponse>());
        }


        public async Task<TeamSeasonArcResult> GetTeamSeasonArcAsync(
            int teamId, int year, CancellationToken token = default)
        {
            var team = await _uow.Teams.GetByTeamIdAsync(teamId, token)
                       ?? throw new KeyNotFoundException($"Team {teamId} not found.");

            var weeks = await _uow.WeeklyRankings.GetByTeamAndYearAsync(teamId, year, token);

            if (!weeks.Any())
                throw new KeyNotFoundException($"No weekly rankings found for team {teamId} in {year}.");

            var arc = weeks.Select(wr => (object)new
            {
                Week = (int)wr.Week,
                Ranking = (double?)wr.Ranking,
                CombinedSOS = (double?)wr.CombinedSOS,
                WinPct = wr.Wins + wr.Losses > 0
                  ? Math.Round((double)wr.Wins / (wr.Wins + wr.Losses), 3) : 0.0
            }).ToList();

            return new TeamSeasonArcResult(teamId, team.TeamName, year, arc);
        }


        // ── Rolling Averages ─────────────────────────────────────────────────────

        /// <summary>
        /// V2: team lookup via Teams + Conferences instead of Team.
        /// TeamRecords and RollingAverageService are shared — unchanged.
        /// </summary>
        public async Task<TeamRollingAveragesResult> GetTeamRollingAveragesV2Async(
            int teamId, int? startYear, CancellationToken token = default)
        {
            var Teams = await _uow.Teams.GetDictionaryByTeamIdAsync(token);
            if (!Teams.TryGetValue(teamId, out var team))
                throw new KeyNotFoundException($"Team {teamId} not found.");

            var confLookup = await _uow.Conferences.GetDictionaryAsync(token);
            string confAbbr = string.Empty;
            if (team.ConferenceId.HasValue && confLookup.TryGetValue(team.ConferenceId.Value, out var conf))
                confAbbr = conf.Abbreviation ?? string.Empty;

            var allRecords = await _uow.TeamRecords.GetByTeamAllYearsAsync(teamId, token);
            if (!allRecords.Any())
                throw new KeyNotFoundException($"No records found for team {teamId}.");

            var history       = allRecords.OrderByDescending(r => r.Year).ToList();
            var targetRecords = startYear.HasValue
                ? allRecords.Where(r => r.Year >= startYear.Value).ToList()
                : allRecords;

            // Same gap as the V1 endpoint: only one team's records were in scope.
            // Reuses the `Teams` dictionary already loaded above for FBS filtering.
            var minYear = targetRecords.Min(r => r.Year) - 10;
            var maxYearExclusive = targetRecords.Max(r => r.Year) + 1;
            var leagueRecords = await _uow.TeamRecords.GetHistoricalAsync(minYear, maxYearExclusive, token);
            var leagueStatsByYear = RollingAverageService.BuildLeagueYearStats(leagueRecords, Teams);

            var results = targetRecords.Select(r =>
            {
                var priorRecords = history.Where(h => h.Year < r.Year).Take(10).ToList();
                var avg = _rollingAverageService.Compute(
                    r, priorRecords, useLiveSwap: false, week: null, leagueStatsByYear);
                return (object)new
                {
                    year = (int)r.Year, wins = (int)r.Wins, losses = (int)r.Losses,
                    seedRating = avg.SeedRating, trendRating = avg.TrendRating,
                    trendHistory = avg.TrendHistory, pedigreeRating = avg.PedigreeRating,
                    pedigreeHistory = avg.PedigreeHistory
                };
            }).ToList();

            return new TeamRollingAveragesResult(team.TeamId, team.TeamName, confAbbr, results);
        }
    }
}
