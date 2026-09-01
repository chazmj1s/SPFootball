using SaturdayPulse.Interfaces;
using System.Collections.Generic;
using System.Linq;
using SaturdayPulse.Models;

namespace SaturdayPulse.Utilities
{
    /// <summary>
    /// Head-to-head, corrected. Behavior, confirmed against official rules for
    /// SEC / Big Ten / Big 12 / MAC / Pac-12:
    ///
    ///   - 2 teams: direct result of the game between them, if played.
    ///   - 3+ teams, complete sub-round-robin (every tied team has played
    ///     every other tied team): win% among those games. A unique leader
    ///     wins outright; a tie (e.g. a 3-way A-beat-B-beat-C-beat-A cycle,
    ///     all 1-1) is inconclusive — NOT resolved by "best win% among tied
    ///     games" the way the original code did.
    ///   - 3+ teams, incomplete sub-round-robin: ONLY a full sweep (beat
    ///     every other tied team) resolves this step. Confirmed explicitly
    ///     for SEC/Big 12 ("if all teams involved in the tie did not play
    ///     each other, but one team defeated all other teams... move to the
    ///     next step" if no sweep exists — no partial-win% fallback).
    ///
    /// Pac-12 is confirmed to skip the sweep-fallback entirely when the
    /// sub-schedule is incomplete ("If not every tied team has played each
    /// other, go to step 2") — set allowSweepWhenIncomplete=false for it.
    /// </summary>
    public class HeadToHeadStep : ITiebreakerStep
    {
        private readonly bool _allowSweepWhenIncomplete;

        public HeadToHeadStep(bool allowSweepWhenIncomplete = true)
        {
            _allowSweepWhenIncomplete = allowSweepWhenIncomplete;
        }

        public string Name => "H2H";

        public TiebreakStepResult Apply(
            List<ConferenceStanding> pool,
            List<ConferenceStanding> fullStandings,
            List<string> log,
            List<string> stubs,
            int spot)
        {
            if (pool.Count == 2)
                return ApplyTwoTeam(pool, log, spot);

            return ApplyMultiTeam(pool, log, spot);
        }

        private TiebreakStepResult ApplyTwoTeam(List<ConferenceStanding> pool, List<string> log, int spot)
        {
            var a = pool[0];
            var b = pool[1];

            if (a.HeadToHeadResults.TryGetValue(b.TeamId, out var aWon))
            {
                var winner = aWon ? a : b;
                log.Add($"  Spot {spot} ({Name}): {winner.TeamName} wins head-to-head");
                return TiebreakStepResult.Resolved(winner);
            }

            log.Add($"  Spot {spot} ({Name}): no head-to-head matchup between tied teams");
            return TiebreakStepResult.NoSeparation(pool);
        }

        private TiebreakStepResult ApplyMultiTeam(List<ConferenceStanding> pool, List<string> log, int spot)
        {
            var poolIds = pool.Select(t => t.TeamId).ToHashSet();

            var records = pool.Select(t =>
            {
                var wins = t.HeadToHeadResults.Count(kvp => poolIds.Contains(kvp.Key) && kvp.Value);
                var losses = t.HeadToHeadResults.Count(kvp => poolIds.Contains(kvp.Key) && !kvp.Value);
                return (Team: t, Wins: wins, Losses: losses);
            }).ToList();

            // Complete sub-round-robin: every tied team has a recorded result
            // against every OTHER tied team (pool.Count - 1 games apiece).
            var isComplete = records.All(r => r.Wins + r.Losses == pool.Count - 1);

            if (isComplete)
            {
                var withPct = records.Select(r => (r.Team, Pct: (double)r.Wins / (r.Wins + r.Losses))).ToList();
                var best = withPct.Max(r => r.Pct);
                var leaders = withPct.Where(r => r.Pct == best).Select(r => r.Team).ToList();

                if (leaders.Count == 1)
                {
                    log.Add($"  Spot {spot} ({Name}): {leaders[0].TeamName} best record in complete round-robin among tied teams ({best:P0})");
                    return TiebreakStepResult.Resolved(leaders[0]);
                }

                log.Add(leaders.Count < pool.Count
                    ? $"  Spot {spot} ({Name}): narrowed to {leaders.Count} team(s) at {best:P0} in round-robin among tied teams"
                    : $"  Spot {spot} ({Name}): no separation — round-robin cycle among tied teams");

                return leaders.Count < pool.Count
                    ? TiebreakStepResult.Narrowed(leaders)
                    : TiebreakStepResult.NoSeparation(pool);
            }

            // Incomplete sub-round-robin: sweep only.
            if (!_allowSweepWhenIncomplete)
            {
                log.Add($"  Spot {spot} ({Name}): tied teams have not all played each other — inconclusive");
                return TiebreakStepResult.NoSeparation(pool);
            }

            var beatAll = records.FirstOrDefault(r => r.Wins == pool.Count - 1);
            if (beatAll.Team != null)
            {
                log.Add($"  Spot {spot} ({Name}): {beatAll.Team.TeamName} beat every other tied team");
                return TiebreakStepResult.Resolved(beatAll.Team);
            }

            log.Add($"  Spot {spot} ({Name}): tied teams have not all played each other and no team swept — inconclusive");
            return TiebreakStepResult.NoSeparation(pool);
        }
    }
}
