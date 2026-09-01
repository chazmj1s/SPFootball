using SaturdayPulse.Interfaces;
using System.Collections.Generic;
using System.Linq;
using SaturdayPulse.Models;

namespace SaturdayPulse.Utilities
{
    /// <summary>
    /// "Cumulative/combined conference winning percentage of all conference
    /// opponents" — strength-of-schedule proxy. Uses ConferenceOpponentWinPct,
    /// which (unlike the old CommonOpponentWinPct) was already computed
    /// correctly — each team's own opponents' average ConferenceWinPct.
    /// </summary>
    public class ConferenceSOSStep : ITiebreakerStep
    {
        public string Name => "Conf opp WP / SOS";

        public TiebreakStepResult Apply(
            List<ConferenceStanding> pool,
            List<ConferenceStanding> fullStandings,
            List<string> log,
            List<string> stubs,
            int spot)
        {
            var best = pool.Max(t => t.ConferenceOpponentWinPct);
            var leaders = pool.Where(t => t.ConferenceOpponentWinPct == best).ToList();

            if (leaders.Count == 1)
            {
                log.Add($"  Spot {spot} ({Name}): {leaders[0].TeamName} ({best:P1})");
                return TiebreakStepResult.Resolved(leaders[0]);
            }

            log.Add(leaders.Count < pool.Count
                ? $"  Spot {spot} ({Name}): narrowed to {leaders.Count} team(s) at {best:P1}"
                : $"  Spot {spot} ({Name}): no separation at {best:P1}");

            return leaders.Count < pool.Count
                ? TiebreakStepResult.Narrowed(leaders)
                : TiebreakStepResult.NoSeparation(pool);
        }
    }

    /// <summary>
    /// "Total wins in a 12-game season, max 1 FCS win counted" — the FCS cap
    /// is assumed already reflected in OverallWins upstream (matches the
    /// prior implementation's assumption; not re-validated here).
    /// </summary>
    public class TotalWinsStep : ITiebreakerStep
    {
        public string Name => "Total wins (max 1 FCS)";

        public TiebreakStepResult Apply(
            List<ConferenceStanding> pool,
            List<ConferenceStanding> fullStandings,
            List<string> log,
            List<string> stubs,
            int spot)
        {
            var best = pool.Max(t => t.OverallWins);
            var leaders = pool.Where(t => t.OverallWins == best).ToList();

            if (leaders.Count == 1)
            {
                log.Add($"  Spot {spot} ({Name}): {leaders[0].TeamName} ({best} wins)");
                return TiebreakStepResult.Resolved(leaders[0]);
            }

            log.Add(leaders.Count < pool.Count
                ? $"  Spot {spot} ({Name}): narrowed to {leaders.Count} team(s) at {best} wins"
                : $"  Spot {spot} ({Name}): no separation at {best} wins");

            return leaders.Count < pool.Count
                ? TiebreakStepResult.Narrowed(leaders)
                : TiebreakStepResult.NoSeparation(pool);
        }
    }

    /// <summary>
    /// Terminal step for conferences whose published procedure ends in a
    /// commissioner's random draw rather than a coin toss between exactly two
    /// teams — functionally identical, kept as a separate named step purely
    /// for log clarity. TiebreakerExecutor's own fallback already performs a
    /// random draw if every step in a conference's list runs out without a
    /// resolution, so this step is only useful when a conference's list
    /// should visibly log the random-draw step before falling off the end
    /// (all of them, currently) — included for explicitness/symmetry with the
    /// published procedures rather than relying on the implicit fallback.
    /// </summary>
    public class RandomDrawStep : ITiebreakerStep
    {
        private static readonly System.Random Rng = new();

        public string Name => "Random draw";

        public TiebreakStepResult Apply(
            List<ConferenceStanding> pool,
            List<ConferenceStanding> fullStandings,
            List<string> log,
            List<string> stubs,
            int spot)
        {
            var winner = pool[Rng.Next(pool.Count)];
            log.Add($"  Spot {spot} ({Name}): {winner.TeamName} selected by random draw among {pool.Count} team(s)");
            return TiebreakStepResult.Resolved(winner);
        }
    }
}
