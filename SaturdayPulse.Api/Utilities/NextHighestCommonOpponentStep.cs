using SaturdayPulse.Interfaces;
using System.Collections.Generic;
using System.Linq;
using SaturdayPulse.Models;

namespace SaturdayPulse.Utilities
{
    /// <summary>
    /// "Record against the next-highest-placed common opponent, proceeding
    /// down through the standings." Confirmed as real, load-bearing criteria
    /// for SEC, Big Ten, Big 12, Pac-12, MAC, and Sun Belt — this step did not
    /// exist anywhere in the prior shared engine (only the Pac-12-specific
    /// resolver had an equivalent).
    ///
    /// Walks fullStandings from the top, grouping teams with equal
    /// ConferenceWinPct into a single cluster per the confirmed "collective
    /// evaluation" rule (Big 12: "When arriving at another group of tied
    /// teams while comparing records, use each team's win percentage against
    /// the collective tied teams as a group... rather than the performance
    /// against individual tied teams" — same language independently confirmed
    /// for Pac-12 and Sun Belt).
    /// </summary>
    public class NextHighestCommonOpponentStep : ITiebreakerStep
    {
        public string Name => "Next-highest common opp";

        public TiebreakStepResult Apply(
            List<ConferenceStanding> pool,
            List<ConferenceStanding> fullStandings,
            List<string> log,
            List<string> stubs,
            int spot)
        {
            var poolIds = pool.Select(t => t.TeamId).ToHashSet();

            var opponentClusters = fullStandings
                .Where(t => !poolIds.Contains(t.TeamId))
                .GroupBy(t => t.ConferenceWinPct)
                .OrderByDescending(g => g.Key)
                .Select(g => g.Select(t => t.TeamId).ToHashSet())
                .ToList();

            foreach (var cluster in opponentClusters)
            {
                var records = pool.Select(t =>
                {
                    var wins = t.HeadToHeadResults.Count(kvp => cluster.Contains(kvp.Key) && kvp.Value);
                    var losses = t.HeadToHeadResults.Count(kvp => cluster.Contains(kvp.Key) && !kvp.Value);
                    return (Team: t, Wins: wins, Losses: losses);
                }).ToList();

                // Every pool team needs at least one recorded result against
                // this cluster to compare fairly — if any team never played
                // anyone in it, this cluster can't separate the pool.
                if (records.Any(r => r.Wins + r.Losses == 0))
                    continue;

                var withPct = records.Select(r => (r.Team, Pct: (double)r.Wins / (r.Wins + r.Losses))).ToList();
                var best = withPct.Max(r => r.Pct);
                var leaders = withPct.Where(r => r.Pct == best).Select(r => r.Team).ToList();

                if (leaders.Count == pool.Count)
                    continue; // no separation vs. this cluster — try the next one down

                if (leaders.Count == 1)
                {
                    log.Add($"  Spot {spot} ({Name}): {leaders[0].TeamName} ({best:P1} vs. next cluster down the standings)");
                    return TiebreakStepResult.Resolved(leaders[0]);
                }

                log.Add($"  Spot {spot} ({Name}): narrowed to {leaders.Count} team(s) vs. next cluster down the standings");
                return TiebreakStepResult.Narrowed(leaders);
            }

            log.Add($"  Spot {spot} ({Name}): no separation found through the standings");
            return TiebreakStepResult.NoSeparation(pool);
        }
    }
}
