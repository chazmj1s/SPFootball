using System.Collections.Generic;
using SaturdayPulse.Models;

namespace SaturdayPulse.Interfaces
{
    /// <summary>
    /// One criterion in a conference's published tiebreaker procedure
    /// (head-to-head, common opponents, SOS, internal rating, etc).
    /// </summary>
    public interface ITiebreakerStep
    {
        /// <summary>Short label used in TiebreakerLog entries, e.g. "H2H", "Common opp WP".</summary>
        string Name { get; }

        /// <summary>
        /// Applies this step to the currently-tied pool.
        /// </summary>
        /// <param name="pool">The teams still tied entering this step.</param>
        /// <param name="fullStandings">
        /// The conference's complete standings (not narrowed for the current
        /// spot) — needed by steps that walk "down the standings" (next-highest
        /// common opponent) or need every team's Division/ConferenceWinPct,
        /// independent of who's currently tied or already qualified.
        /// </param>
        /// <param name="log">Tiebreaker log — the step appends its own line(s).</param>
        /// <param name="stubs">Stub-requirement log — appended when the step can't run due to missing data.</param>
        /// <param name="spot">1 or 2 — which championship-game spot is being resolved, for log labeling only.</param>
        TiebreakStepResult Apply(
            List<ConferenceStanding> pool,
            List<ConferenceStanding> fullStandings,
            List<string> log,
            List<string> stubs,
            int spot);
    }
}
