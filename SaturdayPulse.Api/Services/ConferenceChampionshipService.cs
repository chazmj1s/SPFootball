using System;
using System.Collections.Generic;
using System.Linq;
using SaturdayPulse.Data;
using SaturdayPulse.Models;
using SaturdayPulse.Utilities;

namespace SaturdayPulse.Services
{
    // ─────────────────────────────────────────────────────────────────────────
    // SERVICE
    //
    // Models (ConferenceStanding, ChampionshipQualificationResult, ContenderInfo)
    // live in SaturdayPulse.Api/Models/ — see those files for definitions.
    //
    // Tiebreaker step content/order lives in ConferenceTiebreakerRules.cs —
    // this service just wires the right rule list to the right standings and
    // runs TiebreakerExecutor. See that file for per-conference sourcing notes.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Calculates which teams qualify for each FBS conference championship
    /// game based on the official tiebreaker rules for each conference.
    ///
    /// Every conference — Power Four, Pac-12, every Group of Five conference,
    /// and the generic fallback — is resolved through one shared engine
    /// (TiebreakerExecutor) fed a conference-specific ordered step list
    /// (ConferenceTiebreakerRules). Sun Belt is the one structural exception:
    /// it's still divisionally organized, so its qualifiers come from
    /// resolving each division's top spot independently rather than a single
    /// top-2-by-conference-record table.
    ///
    /// External-ranking tiebreaker criteria (SportSource Analytics Team
    /// Rating Score, CFP/AP poll checks, computer-ranking composites) are
    /// replaced everywhere by ConferenceStanding.InternalRatingScore — the
    /// app's own algorithmic ordinal rank — per direction, so the app's
    /// tiebreaker resolution doesn't depend on any third-party data source.
    /// </summary>
    public class ConferenceChampionshipService
    {
        // ── Public entry point ───────────────────────────────────────────────

        /// <summary>
        /// Given a list of standings for all teams in a conference, returns the
        /// two qualifiers for that conference's championship game.
        /// </summary>
        public ChampionshipQualificationResult GetQualifiers(
            string conference,
            List<ConferenceStanding> standings)
        {
            return conference == "Sun Belt"
                ? GetDivisionWinners_SunBelt(standings)
                : GetTopTwo_SingleTable(standings, conference);
        }

        // ── Single-table conferences (everything except Sun Belt) ───────────

        private ChampionshipQualificationResult GetTopTwo_SingleTable(
            List<ConferenceStanding> standings, string conference)
        {
            var result = new ChampionshipQualificationResult
            {
                Conference = conference,
                Format     = "Top 2 by conference record"
            };

            var sorted = standings.OrderByDescending(t => t.ConferenceWinPct).ToList();
            var log    = result.TiebreakerLog;
            var steps  = ConferenceTiebreakerRules.For(conference);

            // `sorted` (the full conference standings) is passed as
            // fullStandings on every call — steps that walk "down the
            // standings" (next-highest common opponent) need the true overall
            // standings, not whatever subset remains after Qualifier1 is
            // pulled out for spot 2.
            var q1 = TiebreakerExecutor.Resolve(sorted, sorted, steps, log, result.StubsApplied, conference, 1);
            var remaining = sorted.Where(t => t != q1).ToList();
            var q2 = TiebreakerExecutor.Resolve(remaining, sorted, steps, log, result.StubsApplied, conference, 2);

            result.Qualifier1 = q1;
            result.Qualifier2 = q2;
            result.Qualifier1Method = "Conference record";
            result.Qualifier2Method = "Conference record";
            result.Contenders = GetContenders(standings, q1, q2);
            return result;
        }

        // ── Sun Belt: still divisional ───────────────────────────────────────

        /// <summary>
        /// Sun Belt: ONLY FBS conference still using divisions. Each division
        /// winner qualifies — resolved independently through Sun Belt's own
        /// step list (see ConferenceTiebreakerRules.SunBelt).
        /// </summary>
        private ChampionshipQualificationResult GetDivisionWinners_SunBelt(
            List<ConferenceStanding> standings)
        {
            var result = new ChampionshipQualificationResult
            {
                Conference = "Sun Belt",
                Format     = "Division winners (East vs West)"
            };

            var east = standings.Where(t => t.Division == "East")
                                .OrderByDescending(t => t.ConferenceWinPct).ToList();
            var west = standings.Where(t => t.Division == "West")
                                .OrderByDescending(t => t.ConferenceWinPct).ToList();

            if (!east.Any() || !west.Any())
            {
                result.TiebreakerLog.Add("ERROR: Missing division data for Sun Belt");
                return result;
            }

            var steps = ConferenceTiebreakerRules.SunBelt;

            // fullStandings passed as the complete conference standings (both
            // divisions) — NextHighestDivisionOpponentStep/
            // CommonNonDivisionalOpponentsStep filter by division internally.
            var q1 = TiebreakerExecutor.Resolve(east, standings, steps, result.TiebreakerLog, result.StubsApplied, "Sun Belt East", 1);
            var q2 = TiebreakerExecutor.Resolve(west, standings, steps, result.TiebreakerLog, result.StubsApplied, "Sun Belt West", 1);

            result.Qualifier1 = q1;
            result.Qualifier2 = q2;
            result.Qualifier1Method = "East Division winner";
            result.Qualifier2Method = "West Division winner";
            result.Contenders = GetContenders(standings, q1, q2);
            return result;
        }

        // ── Contenders (unchanged from prior implementation) ────────────────

        private static List<ContenderInfo> GetContenders(
            List<ConferenceStanding> allStandings,
            ConferenceStanding qualifier1,
            ConferenceStanding qualifier2)
        {
            if (allStandings.Count == 0) return new();

            // A team is a contender if:
            // - Not already a qualifier
            // - Within 1 game of the #2 qualifier's win pct
            // - Has at least 1 conference game played
            var q2WinPct = qualifier2?.ConferenceWinPct ?? 0.0;
            var cutoff = q2WinPct - (1.0 / Math.Max(1,
                (qualifier2?.ConferenceWins ?? 0) + (qualifier2?.ConferenceLosses ?? 0)));

            return allStandings
                .Where(t => t != qualifier1 &&
                            t != qualifier2 &&
                            t.ConferenceWinPct >= cutoff &&
                            (t.ConferenceWins + t.ConferenceLosses) > 0)
                .OrderByDescending(t => t.ConferenceWinPct)
                .ThenByDescending(t => t.ConferenceWins)
                .Select(t => new ContenderInfo
                {
                    TeamName = t.TeamName,
                    ConferenceWins = t.ConferenceWins,
                    ConferenceLosses = t.ConferenceLosses,
                    ActualConferenceWins = t.ActualConferenceWins,
                    ActualConferenceLosses = t.ActualConferenceLosses
                })
                .ToList();
        }
    }
}
