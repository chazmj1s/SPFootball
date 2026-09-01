using SaturdayPulse.Interfaces;
using System.Collections.Generic;
using System.Linq;
using SaturdayPulse.Models;

namespace SaturdayPulse.Utilities
{
    /// <summary>
    /// "Win percentage against all common conference opponents among the
    /// tied teams." Fixes a real bug in the prior implementation:
    /// ConferenceStanding.CommonOpponentWinPct (now removed) was computed as
    /// each team's win% across its ENTIRE HeadToHeadResults dictionary — i.e.
    /// the same number as ConferenceWinPct. Since this step only ever runs on
    /// a group already tied on ConferenceWinPct, that field could never
    /// separate anyone; it was a mathematically inert step in every
    /// conference that used it.
    ///
    /// "Common opponents" here means the intersection of every tied team's
    /// played opponents, excluding the other tied teams themselves (head-to-
    /// head is a separate, earlier step) — computed live against the CURRENT
    /// pool, since that intersection changes as the pool narrows across
    /// restarts.
    /// </summary>
    public class CommonOpponentsStep : ITiebreakerStep
    {
        public string Name => "Common opp WP";

        public TiebreakStepResult Apply(
            List<ConferenceStanding> pool,
            List<ConferenceStanding> fullStandings,
            List<string> log,
            List<string> stubs,
            int spot)
        {
            var poolIds = pool.Select(t => t.TeamId).ToHashSet();

            var commonOpponentIds = pool
                .Select(t => t.HeadToHeadResults.Keys.Where(id => !poolIds.Contains(id)).ToHashSet())
                .Aggregate((HashSet<int>?)null, (acc, ids) =>
                {
                    if (acc == null) return ids;
                    acc.IntersectWith(ids);
                    return acc;
                }) ?? new HashSet<int>();

            if (commonOpponentIds.Count == 0)
            {
                log.Add($"  Spot {spot} ({Name}): no opponents common to all tied teams");
                return TiebreakStepResult.NoSeparation(pool);
            }

            var records = pool.Select(t =>
            {
                var wins = t.HeadToHeadResults.Count(kvp => commonOpponentIds.Contains(kvp.Key) && kvp.Value);
                var losses = t.HeadToHeadResults.Count(kvp => commonOpponentIds.Contains(kvp.Key) && !kvp.Value);
                var pct = (wins + losses) > 0 ? (double)wins / (wins + losses) : 0.0;
                return (Team: t, Pct: pct);
            }).ToList();

            var best = records.Max(r => r.Pct);
            var leaders = records.Where(r => r.Pct == best).Select(r => r.Team).ToList();

            if (leaders.Count == 1)
            {
                log.Add($"  Spot {spot} ({Name}): {leaders[0].TeamName} ({best:P1} vs. {commonOpponentIds.Count} common opponent(s))");
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
}
