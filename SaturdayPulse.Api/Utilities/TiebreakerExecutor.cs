using SaturdayPulse.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using SaturdayPulse.Models;

namespace SaturdayPulse.Utilities
{
    /// <summary>
    /// Resolves one championship-game spot from a sorted conference standings
    /// list, applying the conference's specific tiebreaker step list when
    /// multiple teams share the top conference win percentage.
    ///
    /// Single engine for every conference (P4, G5, Pac-12, Sun Belt divisions,
    /// generic fallback) — the only thing that varies per conference is which
    /// ITiebreakerStep list ConferenceTiebreakerRules hands it.
    /// </summary>
    public static class TiebreakerExecutor
    {
        public static ConferenceStanding? Resolve(
            List<ConferenceStanding> sorted,
            List<ConferenceStanding> fullStandings,
            IReadOnlyList<ITiebreakerStep> steps,
            List<string> log,
            List<string> stubs,
            string confLabel,
            int spot)
        {
            if (!sorted.Any()) return null;

            var bestPct = sorted.First().ConferenceWinPct;
            var pool = sorted.Where(t => t.ConferenceWinPct == bestPct).ToList();

            if (pool.Count == 1)
            {
                log.Add($"Spot {spot}: {pool[0].TeamName} leads outright ({pool[0].ConferenceWins}-{pool[0].ConferenceLosses})");
                return pool[0];
            }

            log.Add($"Spot {spot}: {pool.Count}-way tie at {bestPct:P0} — applying {confLabel} tiebreakers");
            return ResolvePool(pool, fullStandings, steps, log, stubs, spot);
        }

        /// <summary>
        /// Runs the step list against the given pool. On Narrowed, restarts
        /// at step 1 for the narrowed group (recursively) rather than
        /// continuing to the next step in the list — this is the behavior
        /// confirmed directly from official conference documents (Pac-12:
        /// "after one team has an advantage and is seeded, all remaining
        /// teams... repeat the multiple-team tie-breaking procedure"; Big Ten:
        /// "the remaining teams still in contention revert to the beginning
        /// of the applicable tiebreaker procedures"; same language from SEC,
        /// ACC, and Big 12's own published rules).
        /// </summary>
        private static ConferenceStanding ResolvePool(
            List<ConferenceStanding> pool,
            List<ConferenceStanding> fullStandings,
            IReadOnlyList<ITiebreakerStep> steps,
            List<string> log,
            List<string> stubs,
            int spot)
        {
            foreach (var step in steps)
            {
                var result = step.Apply(pool, fullStandings, log, stubs, spot);

                switch (result.Outcome)
                {
                    case TiebreakOutcome.Resolved:
                        return result.Winner!;

                    case TiebreakOutcome.Narrowed:
                        return ResolvePool(result.Pool, fullStandings, steps, log, stubs, spot);

                    case TiebreakOutcome.NoSeparation:
                    case TiebreakOutcome.Stub:
                    default:
                        continue; // same pool, next step
                }
            }

            log.Add($"Spot {spot}: All tiebreakers exhausted — random selection among {pool.Count} team(s)");
            return pool[new Random().Next(pool.Count)];
        }
    }
}
