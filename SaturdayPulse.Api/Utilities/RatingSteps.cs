using SaturdayPulse.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using SaturdayPulse.Models;

namespace SaturdayPulse.Utilities
{
    /// <summary>
    /// Stands in for every external-ranking tiebreaker step across every
    /// conference (SportSource Analytics Team Rating Score, CFP/AP poll
    /// checks, computer-ranking composites) with the app's own algorithmic
    /// ordinal — ConferenceStanding.InternalRatingScore, sourced from
    /// GetPowerRankingsV2Async's OverallRank (1 = best).
    ///
    /// NOTE: the "highest CFP-ranked team that doesn't lose its final game"
    /// eligibility-gate mechanic used by AAC/Mountain West/C-USA/Sun Belt's
    /// real procedures is deliberately NOT reconstructed here — there's no
    /// clean analog to "hasn't been polled" for an algorithm that rates every
    /// team every week, and per direction, comparing internal ratings
    /// directly is simpler and more in the spirit of an independent algorithm
    /// than simulating a human committee's polling behavior.
    /// </summary>
    public class InternalRatingStep : ITiebreakerStep
    {
        public string Name => "Internal rating";

        public TiebreakStepResult Apply(
            List<ConferenceStanding> pool,
            List<ConferenceStanding> fullStandings,
            List<string> log,
            List<string> stubs,
            int spot)
        {
            var withRating = pool.Where(t => t.InternalRatingScore.HasValue).ToList();

            if (withRating.Count != pool.Count)
            {
                stubs.Add($"Spot {spot}: Internal rating tiebreaker required but rating unavailable for one or more tied teams");
                log.Add($"  Spot {spot} ({Name}): STUB — rating unavailable for one or more tied teams");
                return TiebreakStepResult.Stub(pool);
            }

            var best = withRating.Min(t => t.InternalRatingScore!.Value); // 1 = best
            var leaders = withRating.Where(t => t.InternalRatingScore == best).ToList();

            if (leaders.Count == 1)
            {
                log.Add($"  Spot {spot} ({Name}): {leaders[0].TeamName} (rank #{best})");
                return TiebreakStepResult.Resolved(leaders[0]);
            }

            log.Add(leaders.Count < pool.Count
                ? $"  Spot {spot} ({Name}): narrowed to {leaders.Count} team(s) at rank #{best}"
                : $"  Spot {spot} ({Name}): no separation at rank #{best}");

            return leaders.Count < pool.Count
                ? TiebreakStepResult.Narrowed(leaders)
                : TiebreakStepResult.NoSeparation(pool);
        }
    }

    /// <summary>
    /// SEC step 5: capped relative total scoring margin vs. all conference
    /// opponents. Cap: 42 points scored (offense) / 48 points allowed
    /// (defense), applied PER GAME before averaging — confirmed via the SEC's
    /// own announcement text. Requires ConferenceStanding.ConferenceGameScores
    /// (per-game data); stubs out if any tied team is missing it rather than
    /// silently approximating from a season aggregate, since capping
    /// specifically exists to prevent a single blowout from dominating the
    /// result and an aggregate can't be uncapped after the fact.
    /// </summary>
    public class CappedScoringMarginStep : ITiebreakerStep
    {
        private const int OffenseCap = 42;
        private const int DefenseCap = 48;

        public string Name => "Capped scoring margin";

        public TiebreakStepResult Apply(
            List<ConferenceStanding> pool,
            List<ConferenceStanding> fullStandings,
            List<string> log,
            List<string> stubs,
            int spot)
        {
            if (pool.Any(t => t.ConferenceGameScores == null || t.ConferenceGameScores.Count == 0))
            {
                stubs.Add($"Spot {spot}: Capped scoring margin tiebreaker required but per-game score data unavailable for one or more tied teams");
                log.Add($"  Spot {spot} ({Name}): STUB — per-game score data unavailable");
                return TiebreakStepResult.Stub(pool);
            }

            var margins = pool.Select(t =>
            {
                var cappedMargins = t.ConferenceGameScores.Select(g =>
                    Math.Min(g.PointsFor, OffenseCap) - Math.Min(g.PointsAgainst, DefenseCap));
                return (Team: t, AvgMargin: cappedMargins.Average());
            }).ToList();

            var best = margins.Max(m => m.AvgMargin);
            var leaders = margins.Where(m => m.AvgMargin == best).Select(m => m.Team).ToList();

            if (leaders.Count == 1)
            {
                log.Add($"  Spot {spot} ({Name}): {leaders[0].TeamName} ({best:F1} avg capped margin)");
                return TiebreakStepResult.Resolved(leaders[0]);
            }

            log.Add(leaders.Count < pool.Count
                ? $"  Spot {spot} ({Name}): narrowed to {leaders.Count} team(s) at {best:F1}"
                : $"  Spot {spot} ({Name}): no separation at {best:F1}");

            return leaders.Count < pool.Count
                ? TiebreakStepResult.Narrowed(leaders)
                : TiebreakStepResult.NoSeparation(pool);
        }
    }
}
