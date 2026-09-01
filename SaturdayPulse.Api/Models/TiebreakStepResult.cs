using System.Collections.Generic;

namespace SaturdayPulse.Models
{
    public enum TiebreakOutcome
    {
        /// <summary>Exactly one team remains — the tie at this spot is resolved.</summary>
        Resolved,

        /// <summary>
        /// The step separated the pool into a smaller-but-still-tied group
        /// (2+ teams). Per confirmed official-rule behavior (Pac-12, SEC,
        /// Big Ten, ACC, Big 12 all document this explicitly), the applicable
        /// tiebreaker procedure restarts at step 1 for the narrowed group —
        /// it does NOT continue to the next step in the list with the
        /// narrowed pool.
        /// </summary>
        Narrowed,

        /// <summary>The step produced no separation at all — proceed to the next step, same pool.</summary>
        NoSeparation,

        /// <summary>The step needed data that isn't available for one or more tied teams — proceed to the next step, same pool.</summary>
        Stub
    }

    public class TiebreakStepResult
    {
        public TiebreakOutcome Outcome { get; }
        public ConferenceStanding? Winner { get; }
        public List<ConferenceStanding> Pool { get; }

        private TiebreakStepResult(TiebreakOutcome outcome, List<ConferenceStanding> pool, ConferenceStanding? winner)
        {
            Outcome = outcome;
            Pool = pool;
            Winner = winner;
        }

        public static TiebreakStepResult Resolved(ConferenceStanding winner) =>
            new(TiebreakOutcome.Resolved, new List<ConferenceStanding> { winner }, winner);

        public static TiebreakStepResult Narrowed(List<ConferenceStanding> pool) =>
            new(TiebreakOutcome.Narrowed, pool, null);

        public static TiebreakStepResult NoSeparation(List<ConferenceStanding> pool) =>
            new(TiebreakOutcome.NoSeparation, pool, null);

        public static TiebreakStepResult Stub(List<ConferenceStanding> pool) =>
            new(TiebreakOutcome.Stub, pool, null);
    }
}
