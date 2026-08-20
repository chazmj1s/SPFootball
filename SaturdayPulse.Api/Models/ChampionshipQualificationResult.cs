using System.Collections.Generic;

namespace SaturdayPulse.Models
{
    /// <summary>
    /// Result of a championship qualification calculation for one conference.
    /// </summary>
    public class ChampionshipQualificationResult
    {
        public string Conference   { get; set; }
        public string Format       { get; set; }  // "Top 2" | "Division Winners" | etc.

        // The two qualifiers (or one per division for division-based conferences)
        public ConferenceStanding Qualifier1 { get; set; }
        public ConferenceStanding Qualifier2 { get; set; }

        // How the spots were determined
        public string Qualifier1Method { get; set; }
        public string Qualifier2Method { get; set; }

        // Any tiebreaker steps that were used
        public List<string> TiebreakerLog { get; set; } = new();

        // Flags for stubs — requirements we couldn't compute from available data
        public List<string> StubsApplied { get; set; } = new();

        public List<ContenderInfo> Contenders { get; set; } = new();
    }
}
