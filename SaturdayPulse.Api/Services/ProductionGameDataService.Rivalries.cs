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
    /// ProductionGameDataService — Rivalries.
    /// Curated rivalry lookups, matchup history, and the RivalryNotes builder
    /// shared by GetScheduleV2Async and the postseason title-game card.
    /// </summary>
    public partial class ProductionGameDataService
    {

        // ── Rivalries ────────────────────────────────────────────────────────────

        public async Task<RivalriesResult> GetRivalriesAsync(
            string? tier, int? minGames, double? minVarianceRatio, CancellationToken token = default)
        {
            var matchups = await _uow.Lookups.GetMatchupHistoriesAsync(token);

            if (!string.IsNullOrEmpty(tier) && !tier.Equals("ALL", StringComparison.OrdinalIgnoreCase))
                matchups = matchups.Where(m => m.RivalryTier == tier).ToList();
            if (minGames.HasValue)
                matchups = matchups.Where(m => m.GamesPlayed >= minGames.Value).ToList();

            matchups = matchups.OrderByDescending(m => m.GamesPlayed).ToList();

            var teamsById = await _uow.Teams.GetDictionaryByTeamIdAsync(token);
            var asdList   = await _uow.Lookups.GetAvgScoreDeltasAsync(token);
            var avgStDev  = asdList.Any() ? asdList.Average(a => (double)a.StDevP) : 15.0;

            var results = new List<object>();
            foreach (var m in matchups)
            {
                var team1         = teamsById.TryGetValue(m.Team1Id, out var t1) ? t1.TeamName : "Unknown";
                var team2         = teamsById.TryGetValue(m.Team2Id, out var t2) ? t2.TeamName : "Unknown";
                var varianceRatio = (double)m.StDevMargin / avgStDev;

                if (minVarianceRatio.HasValue && varianceRatio < minVarianceRatio.Value) continue;

                results.Add(new
                {
                    team1, team2,
                    rivalryName   = m.RivalryName ?? "N/A",
                    tier          = m.RivalryTier ?? "N/A",
                    gamesPlayed   = m.GamesPlayed,
                    avgMargin     = Math.Round((double)m.AvgMargin,   1),
                    stDevMargin   = Math.Round((double)m.StDevMargin, 1),
                    upsetRate     = Math.Round((double)m.UpsetRate,   3),
                    varianceRatio = Math.Round(varianceRatio,         2),
                    seriesAge     = m.LastPlayed - m.FirstPlayed,
                    firstPlayed   = m.FirstPlayed,
                    lastPlayed    = m.LastPlayed
                });
            }

            return new RivalriesResult(results.Count, matchups.Count,
                new { tier = tier ?? "ALL", minGames = minGames ?? 0, minVarianceRatio = minVarianceRatio ?? 0.0 },
                results);
        }


        // ── Teams and Rivalries ──────────────────────────────────────────────────

        /// <summary>
        /// V2: reads rivalry game history from Games table (CFBD-sourced).
        /// Legacy equivalent: GetRivalryHistoryAsync() which reads from Game table.
        ///
        /// Team lookup via Teams + Conferences instead of Team.
        /// Winner/loser derived from home/away points; home team defaults for unplayed.
        /// Projection and rivalry metadata (MatchupHistory, AvgScoreDeltas) unchanged —
        /// those tables are shared and will be rebuilt via Developer backfill.
        /// </summary>
        public async Task<RivalryHistoryResult> GetRivalryHistoryV2Async(
            int team1Id, int team2Id, int years, CancellationToken token = default)
        {
            var Teams    = await _uow.Teams.GetDictionaryByTeamIdAsync(token);
            var confLookup = await _uow.Conferences.GetDictionaryAsync(token);

            if (!Teams.TryGetValue(team1Id, out var team1))
                throw new KeyNotFoundException($"Team {team1Id} not found.");
            if (!Teams.TryGetValue(team2Id, out var team2))
                throw new KeyNotFoundException($"Team {team2Id} not found.");

            string ConfAbbr(Teams? t)
            {
                if (t?.ConferenceId == null) return string.Empty;
                confLookup.TryGetValue(t.ConferenceId.Value, out var conf);
                return conf?.Abbreviation ?? string.Empty;
            }

            var cutoffYear     = DateTime.Now.Year - years;
            var games          = await _uow.Games.GetRivalryHistoryAsync(team1Id, team2Id, cutoffYear, token);
            var rivalry        = await _uow.Lookups.GetMatchupHistoryAsync(team1Id, team2Id, token);
            var avgScoreDeltas = await _uow.Lookups.GetAvgScoreDeltasAsync(token);

            var avgTeamScore = games.Count > 0
                ? (games.Average(g => g.HomePoints ?? 0) + games.Average(g => g.AwayPoints ?? 0)) / 2.0
                : 28.0;

            var history = games
                .Where(g => (g.HomePoints ?? 0) > 0 || (g.AwayPoints ?? 0) > 0)
                .Select(g =>
                {
                    bool team1IsHome = g.HomeId == team1Id;
                    var team1Score   = team1IsHome ? (g.HomePoints ?? 0) : (g.AwayPoints ?? 0);
                    var team2Score   = team1IsHome ? (g.AwayPoints ?? 0) : (g.HomePoints ?? 0);
                    bool team1Won    = team1Score > team2Score;
                    char location    = g.NeutralSite == true ? 'N' : team1IsHome ? 'H' : 'A';

                    return (object)new
                    {
                        g.Year,
                        g.Week,
                        Location   = location,
                        Team1Score = team1Score,
                        Team2Score = team2Score,
                        Margin     = team1Score - team2Score,
                        ActualOU   = team1Score + team2Score,
                        Team1Won   = team1Won,
                        WinnerName = team1Won ? team1.TeamName : team2.TeamName,
                        Score      = $"{Math.Max(team1Score, team2Score)}-{Math.Min(team1Score, team2Score)}"
                    };
                }).ToList();

            object? projection = null;
            var currentYear = (short)DateTime.Now.Year;
            var t1Record    = await _uow.TeamRecords.GetByTeamAndYearAsync(team1Id, currentYear, token);
            var t2Record    = await _uow.TeamRecords.GetByTeamAndYearAsync(team2Id, currentYear, token);

            if (t1Record != null && t2Record != null)
            {
                var t1WinPct    = RatingCalculator.BucketWinPct(t1Record.Wins, t1Record.Wins + t1Record.Losses);
                var t2WinPct    = RatingCalculator.BucketWinPct(t2Record.Wins, t2Record.Wins + t2Record.Losses);
                var maxPct      = Math.Max(t1WinPct, t2WinPct);
                var minPct      = Math.Min(t1WinPct, t2WinPct);
                var asd         = avgScoreDeltas.FirstOrDefault(a => a.Team1WinPct == maxPct && a.Team2WinPct == minPct);
                var delta       = asd != null && asd.SampleSize >= 10
                    ? Math.Max(-35.0, Math.Min(35.0, (double)asd.AverageScoreDelta)) : AvgScoreDelta.DefaultAverageScoreDelta;
                var deltaFromT1 = RatingCalculator.ExpectedFromPerspective(delta, t1WinPct, t2WinPct);
                if (t1Record.Ranking.HasValue && t2Record.Ranking.HasValue)
                    deltaFromT1 += (double)(t1Record.Ranking.Value - t2Record.Ranking.Value) * 0.15;

                var projT1 = Math.Round(avgTeamScore + deltaFromT1 / 2.0, 1);
                var projT2 = Math.Round(avgTeamScore - deltaFromT1 / 2.0, 1);

                projection = new
                {
                    Year           = currentYear,
                    ProjTeam1Score = projT1,
                    ProjTeam2Score = projT2,
                    ProjMargin     = Math.Round(projT1 - projT2, 1),
                    ProjOU         = Math.Round(projT1 + projT2, 1),
                    IsProjected    = true
                };
            }

            return new RivalryHistoryResult(
                team1Id, team1.TeamName, team1.Abbreviation ?? team1.TeamName,
                team2Id, team2.TeamName, team2.Abbreviation ?? team2.TeamName,
                rivalry?.RivalryName, rivalry?.RivalryTier,
                rivalry?.GamesPlayed ?? history.Count,
                rivalry?.AvgMargin, rivalry?.UpsetRate,
                history, projection);
        }


        // ── Named Rivalries ──────────────────────────────────────────────────────

        /// <summary>
        /// V2: team name lookup via Teams instead of Team.
        /// MatchupHistory table is shared — unchanged.
        /// </summary>
        public async Task<NamedRivalriesResult> GetNamedRivalriesV2Async(CancellationToken token = default)
        {
            var rivalries = await _uow.Lookups.GetMatchupHistoriesAsync(token);
            rivalries = rivalries
                .Where(m => m.RivalryName != null)
                .OrderBy(m => m.RivalryTier).ThenBy(m => m.RivalryName)
                .ToList();

            var Teams = await _uow.Teams.GetDictionaryByTeamIdAsync(token);

            var result = rivalries.Select(r =>
            {
                Teams.TryGetValue(r.Team1Id, out var t1);
                Teams.TryGetValue(r.Team2Id, out var t2);
                return (object)new
                {
                    r.Team1Id,
                    Team1Name      = t1?.TeamName ?? "Unknown",
                    Team1ShortName = t1?.Abbreviation ?? t1?.TeamName ?? "Unknown",
                    r.Team2Id,
                    Team2Name      = t2?.TeamName ?? "Unknown",
                    Team2ShortName = t2?.Abbreviation ?? t2?.TeamName ?? "Unknown",
                    r.RivalryName, r.RivalryTier, r.GamesPlayed,
                    r.AvgMargin, r.StDevMargin, r.UpsetRate, r.FirstPlayed, r.LastPlayed
                };
            }).ToList();

            return new NamedRivalriesResult(result);
        }


        // ════════════════════════════════════════════════════════════════════════
        // Rivalry Notes — Scores + My Teams game card only. NOT the Sandbox footer
        // (GamePredictionService.BuildConfidenceExplanation) — that one deliberately
        // never names a rivalry, since Sandbox matchups are hypothetical and can pair
        // any two team-seasons. This card is the opposite case: a real, scheduled game
        // between two teams that actually are one of the 52 curated MatchupHistory
        // pairs, so naming the rivalry here is the entire point.
        //
        // Grid fields map directly to MatchupHistory (Layer 1/2 backfill from earlier
        // in this project): RivalryName, FirstPlayed, AvgMargin ("Average Spread"),
        // AvgTotalPoints ("Average O/U"), UpsetRate ("Chance of upset").
        //
        // Blurb compares this specific game's actual result (if played) or current
        // projection (if not) against the rivalry's historical averages — plain
        // numbers side by side, no "closer than" / "notably higher" editorializing,
        // since that would need a new threshold to decide what counts as "notable"
        // and the numbers speak for themselves. Deliberately no superlative claims
        // about any rivalry being the best/greatest/etc. — every curated pair gets
        // the same neutral treatment.
        // ════════════════════════════════════════════════════════════════════════

        internal static object? BuildRivalryNotes(
             MatchupHistory? rivalry,
             bool isFinal,
             bool isInProgress,
             double? actualMargin,
             double? actualTotal,
             double? projectedMargin,
             double? projectedTotal,
             string team1,
             string team2,
             string winner)
        {
            if (rivalry == null) return null;

            var avgMargin = (double)rivalry.AvgMargin;
            var avgTotal = (double)rivalry.AvgTotalPoints;
            var threshold = avgMargin * 0.25;

            string blurb;
            if (isFinal && actualMargin.HasValue && actualTotal.HasValue)
            {
                var margin = actualMargin.Value;
                var total = actualTotal.Value;

                if (margin < avgMargin - threshold)
                {
                    blurb =
                        $"Closer than history suggested. {winner} won by {margin:F0} points " +
                        $"on {total:F0} combined — tighter than the series norm of " +
                        $"{avgMargin:F0}-point margins and {avgTotal:F0}-point totals.";
                }
                else if (margin > avgMargin + threshold)
                {
                    blurb =
                        $"More decisive than history suggested. {winner} pulled away by " +
                        $"{margin:F0} points on {total:F0} combined — wider than the " +
                        $"series norm of {avgMargin:F0}-point margins and {avgTotal:F0}-point totals.";
                }
                else
                {
                    blurb =
                        $"Right in line with history. {winner} won by {margin:F0} points " +
                        $"on {total:F0} combined — consistent with the series norm of " +
                        $"{avgMargin:F0}-point margins and {avgTotal:F0}-point totals.";
                }
            }
            else if (isInProgress && actualMargin.HasValue && actualTotal.HasValue)
            {
                var margin = actualMargin.Value;
                var total = actualTotal.Value;

                blurb =
                    $"In progress. {winner} currently leads by {margin:F0} points " +
                    $"on {total:F0} combined so far — the series norm is " +
                    $"{avgMargin:F0}-point margins and {avgTotal:F0}-point totals.";
            }
            else if (!isFinal && !isInProgress && projectedMargin.HasValue && projectedTotal.HasValue)
            {
                var margin = projectedMargin.Value;
                var total = projectedTotal.Value;

                if (margin < avgMargin - threshold)
                {
                    blurb =
                        $"Tighter than history suggests. {winner} is projected to win by " +
                        $"{margin:F0} points on {total:F0} combined — below the " +
                        $"series norm of {avgMargin:F0}-point margins and {avgTotal:F0}-point totals.";
                }
                else if (margin > avgMargin + threshold)
                {
                    blurb =
                        $"More decisive than history suggests. {winner} is projected to win " +
                        $"by {margin:F0} points on {total:F0} combined — wider than " +
                        $"the series norm of {avgMargin:F0}-point margins and {avgTotal:F0}-point totals.";
                }
                else
                {
                    blurb =
                        $"Right in line with history. {winner} is projected to win by " +
                        $"{margin:F0} points on {total:F0} combined — consistent " +
                        $"with the series norm of {avgMargin:F0}-point margins and {avgTotal:F0}-point totals.";
                }
            }
            else
            {
                // No projection available and the game hasn't been played yet —
                // fall back to a plain historical statement with no comparison.
                blurb =
                    $"This matchup has historically been decided by about {avgMargin:F0} " +
                    $"points, with a total near {avgTotal:F0} and an upset in roughly " +
                    $"{rivalry.UpsetRate:P0} of meetings.";
            }

            var favored = rivalry.Team1Wins > rivalry.Team2Wins ? team1 : team2;
            return new
            {
                RivalryName = rivalry.RivalryName,
                FirstPlayed = rivalry.FirstPlayed,
                AverageSpread = Math.Round((double)rivalry.AvgMargin, 2),
                AverageOverUnder = Math.Round((double)rivalry.AvgTotalPoints, 2),
                UpsetChance = Math.Round((double)rivalry.UpsetRate, 2),
                Blurb = blurb,
                Series = $"Series: {Math.Max(rivalry.Team1Wins, rivalry.Team2Wins)} - {Math.Min(rivalry.Team1Wins, rivalry.Team2Wins)} - {rivalry.Ties}, {favored}"
            };
        }
    }
}
