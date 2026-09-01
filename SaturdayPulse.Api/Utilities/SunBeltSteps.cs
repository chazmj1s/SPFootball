using SaturdayPulse.Interfaces;
using System.Collections.Generic;
using System.Linq;
using SaturdayPulse.Models;

namespace SaturdayPulse.Utilities
{
    /// <summary>
    /// Sun Belt step: "overall divisional winning percentage" — win% against
    /// opponents in the same division as the team being evaluated. Computed
    /// live from HeadToHeadResults + fullStandings' Division lookup rather
    /// than needing a new stored field.
    /// </summary>
    public class DivisionalWinPctStep : ITiebreakerStep
    {
        public string Name => "Divisional WP";

        public TiebreakStepResult Apply(
            List<ConferenceStanding> pool,
            List<ConferenceStanding> fullStandings,
            List<string> log,
            List<string> stubs,
            int spot)
        {
            var divisionByTeamId = fullStandings.ToDictionary(t => t.TeamId, t => t.Division);

            var records = pool.Select(t =>
            {
                var wins = t.HeadToHeadResults.Count(kvp =>
                    divisionByTeamId.TryGetValue(kvp.Key, out var d) && d == t.Division && kvp.Value);
                var losses = t.HeadToHeadResults.Count(kvp =>
                    divisionByTeamId.TryGetValue(kvp.Key, out var d) && d == t.Division && !kvp.Value);
                var pct = (wins + losses) > 0 ? (double)wins / (wins + losses) : 0.0;
                return (Team: t, Pct: pct);
            }).ToList();

            var best = records.Max(r => r.Pct);
            var leaders = records.Where(r => r.Pct == best).Select(r => r.Team).ToList();

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
    /// Sun Belt step: "record vs. the next-highest positioned team in the
    /// division, proceeding down" — same collective-tied-cluster handling as
    /// NextHighestCommonOpponentStep, but walking only same-division
    /// opponents rather than the whole conference.
    /// </summary>
    public class NextHighestDivisionOpponentStep : ITiebreakerStep
    {
        public string Name => "Next-highest division opp";

        public TiebreakStepResult Apply(
            List<ConferenceStanding> pool,
            List<ConferenceStanding> fullStandings,
            List<string> log,
            List<string> stubs,
            int spot)
        {
            var poolIds = pool.Select(t => t.TeamId).ToHashSet();
            // All tied teams share a division at this point (Sun Belt resolves
            // qualifiers per-division), so any pool member's Division applies.
            var division = pool.FirstOrDefault()?.Division;

            var opponentClusters = fullStandings
                .Where(t => !poolIds.Contains(t.TeamId) && t.Division == division)
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

                if (records.Any(r => r.Wins + r.Losses == 0))
                    continue;

                var withPct = records.Select(r => (r.Team, Pct: (double)r.Wins / (r.Wins + r.Losses))).ToList();
                var best = withPct.Max(r => r.Pct);
                var leaders = withPct.Where(r => r.Pct == best).Select(r => r.Team).ToList();

                if (leaders.Count == pool.Count)
                    continue;

                if (leaders.Count == 1)
                {
                    log.Add($"  Spot {spot} ({Name}): {leaders[0].TeamName} ({best:P1} vs. next division cluster down)");
                    return TiebreakStepResult.Resolved(leaders[0]);
                }

                log.Add($"  Spot {spot} ({Name}): narrowed to {leaders.Count} team(s) vs. next division cluster down");
                return TiebreakStepResult.Narrowed(leaders);
            }

            log.Add($"  Spot {spot} ({Name}): no separation found through the division standings");
            return TiebreakStepResult.NoSeparation(pool);
        }
    }

    /// <summary>
    /// Sun Belt step: "combined winning percentage against all common
    /// NON-divisional conference opponents" — same intersection approach as
    /// CommonOpponentsStep, but the common-opponent set is restricted to
    /// opponents outside the tied teams' own division.
    /// </summary>
    public class CommonNonDivisionalOpponentsStep : ITiebreakerStep
    {
        public string Name => "Common non-divisional opp WP";

        public TiebreakStepResult Apply(
            List<ConferenceStanding> pool,
            List<ConferenceStanding> fullStandings,
            List<string> log,
            List<string> stubs,
            int spot)
        {
            var poolIds = pool.Select(t => t.TeamId).ToHashSet();
            var divisionByTeamId = fullStandings.ToDictionary(t => t.TeamId, t => t.Division);
            var division = pool.FirstOrDefault()?.Division;

            var commonOpponentIds = pool
                .Select(t => t.HeadToHeadResults.Keys
                    .Where(id => !poolIds.Contains(id) &&
                                 divisionByTeamId.TryGetValue(id, out var d) && d != division)
                    .ToHashSet())
                .Aggregate((HashSet<int>?)null, (acc, ids) =>
                {
                    if (acc == null) return ids;
                    acc.IntersectWith(ids);
                    return acc;
                }) ?? new HashSet<int>();

            if (commonOpponentIds.Count == 0)
            {
                log.Add($"  Spot {spot} ({Name}): no non-divisional opponents common to all tied teams");
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
}
