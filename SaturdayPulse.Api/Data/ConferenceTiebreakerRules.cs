using System.Collections.Generic;
using SaturdayPulse.Interfaces;
using SaturdayPulse.Utilities;

namespace SaturdayPulse.Data
{
    /// <summary>
    /// Per-conference ordered tiebreaker step lists. Each list is confirmed
    /// against the conference's own published procedure (see conversation/
    /// session notes for sourcing) — not a shared generic pipeline with minor
    /// variations. Step content and order genuinely differ conference to
    /// conference.
    /// </summary>
    public static class ConferenceTiebreakerRules
    {
        // Shared step instances — all are stateless, safe to reuse across conferences.
        private static readonly ITiebreakerStep HeadToHead = new HeadToHeadStep();
        private static readonly ITiebreakerStep HeadToHeadNoSweepFallback = new HeadToHeadStep(allowSweepWhenIncomplete: false);
        private static readonly ITiebreakerStep CommonOpponents = new CommonOpponentsStep();
        private static readonly ITiebreakerStep NextHighestCommonOpponent = new NextHighestCommonOpponentStep();
        private static readonly ITiebreakerStep ConferenceSOS = new ConferenceSOSStep();
        private static readonly ITiebreakerStep TotalWins = new TotalWinsStep();
        private static readonly ITiebreakerStep CappedScoringMargin = new CappedScoringMarginStep();
        private static readonly ITiebreakerStep InternalRating = new InternalRatingStep();
        private static readonly ITiebreakerStep RandomDraw = new RandomDrawStep();

        private static readonly ITiebreakerStep DivisionalWinPct = new DivisionalWinPctStep();
        private static readonly ITiebreakerStep NextHighestDivisionOpponent = new NextHighestDivisionOpponentStep();
        private static readonly ITiebreakerStep CommonNonDivisionalOpponents = new CommonNonDivisionalOpponentsStep();

        /// <summary>
        /// SEC: H2H (sweep-or-inconclusive) → common opp → next-highest
        /// common opp → conf SOS → capped scoring margin → random draw.
        /// </summary>
        private static readonly IReadOnlyList<ITiebreakerStep> Sec = new[]
        {
            HeadToHead, CommonOpponents, NextHighestCommonOpponent, ConferenceSOS, CappedScoringMargin, RandomDraw
        };

        /// <summary>
        /// Big Ten: H2H → common opp → common opp by order of finish → conf
        /// SOS → internal rating → random draw.
        /// </summary>
        private static readonly IReadOnlyList<ITiebreakerStep> BigTen = new[]
        {
            HeadToHead, CommonOpponents, NextHighestCommonOpponent, ConferenceSOS, InternalRating, RandomDraw
        };

        /// <summary>
        /// ACC: new 2026 two-stage policy — H2H, then internal rating
        /// ("body of work") decides everything H2H doesn't.
        /// </summary>
        private static readonly IReadOnlyList<ITiebreakerStep> Acc = new[]
        {
            HeadToHead, InternalRating, RandomDraw
        };

        /// <summary>
        /// Big 12: H2H (sweep-or-inconclusive) → common opp → next-highest
        /// common opp → conf SOS → total wins → internal rating → random draw.
        /// </summary>
        private static readonly IReadOnlyList<ITiebreakerStep> Big12 = new[]
        {
            HeadToHead, CommonOpponents, NextHighestCommonOpponent, ConferenceSOS, TotalWins, InternalRating, RandomDraw
        };

        /// <summary>
        /// Pac-12: H2H (NO sweep fallback when incomplete — confirmed
        /// distinct from SEC/Big 12) → common opp → next-highest common opp
        /// → conf SOS → total wins → internal rating → random draw.
        /// </summary>
        private static readonly IReadOnlyList<ITiebreakerStep> Pac12 = new[]
        {
            HeadToHeadNoSweepFallback, CommonOpponents, NextHighestCommonOpponent, ConferenceSOS, TotalWins, InternalRating, RandomDraw
        };

        /// <summary>
        /// AAC / Mountain West / C-USA: confirmed to share one template —
        /// H2H → [CFP-ranked-and-undefeated-final-week check, collapsed to
        /// internal rating per direction] → internal rating. No
        /// common-opponents step exists in any of these three real
        /// procedures.
        /// </summary>
        private static readonly IReadOnlyList<ITiebreakerStep> CfpCheckConferences = new[]
        {
            HeadToHead, InternalRating, RandomDraw
        };

        /// <summary>
        /// MAC: H2H (sweep-or-inconclusive) → common opp → internal rating
        /// → common opp by order of finish → conf SOS → random draw.
        /// NOTE the internal rating step sits mid-list here, not last —
        /// confirmed directly from the MAC's own published procedure, unlike
        /// every other conference which reaches for it last.
        /// </summary>
        private static readonly IReadOnlyList<ITiebreakerStep> Mac = new[]
        {
            HeadToHead, CommonOpponents, InternalRating, NextHighestCommonOpponent, ConferenceSOS, RandomDraw
        };

        /// <summary>
        /// Sun Belt: still divisional. Intra-division order — H2H →
        /// divisional WP → next-highest division opponent → common
        /// non-divisional opp → [CFP-check collapsed to] internal rating →
        /// random draw.
        /// </summary>
        private static readonly IReadOnlyList<ITiebreakerStep> SunBeltSteps = new[]
        {
            HeadToHead, DivisionalWinPct, NextHighestDivisionOpponent, CommonNonDivisionalOpponents, InternalRating, RandomDraw
        };

        /// <summary>
        /// Fallback for any conference not explicitly listed — mirrors the
        /// shape of the majority (SEC/Big Ten/Big 12) pattern rather than
        /// silently reusing one specific conference's real rules.
        /// </summary>
        private static readonly IReadOnlyList<ITiebreakerStep> Generic = new[]
        {
            HeadToHead, CommonOpponents, NextHighestCommonOpponent, ConferenceSOS, InternalRating, RandomDraw
        };

        public static IReadOnlyList<ITiebreakerStep> For(string conference) => conference switch
        {
            "SEC"           => Sec,
            "Big Ten"       => BigTen,
            "ACC"           => Acc,
            "Big 12"        => Big12,
            "Pac-12"        => Pac12,
            "AAC"           => CfpCheckConferences,
            "Mountain West" => CfpCheckConferences,
            "C-USA"         => CfpCheckConferences,
            "MAC"           => Mac,
            _               => Generic
        };

        /// <summary>Sun Belt is structurally divisional, resolved separately — see ConferenceChampionshipService.</summary>
        public static IReadOnlyList<ITiebreakerStep> SunBelt => SunBeltSteps;
    }
}
