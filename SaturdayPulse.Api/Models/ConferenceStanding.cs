using System.Collections.Generic;

namespace SaturdayPulse.Models
{
    /// <summary>
    /// Represents a team's standing within its conference for championship
    /// qualification purposes.
    /// </summary>
    public class ConferenceStanding
    {
        public int    TeamId          { get; set; }
        public string TeamName        { get; set; }
        public string Conference      { get; set; }
        public string Division        { get; set; }  // null if conference has no divisions
        public int    ConferenceWins { get; set; }
        public int    ConferenceLosses { get; set; }
        public int    ActualConferenceWins { get; set; }
        public int    ActualConferenceLosses { get; set; }
        public int    OverallWins     { get; set; }
        public int    OverallLosses   { get; set; }

        // Points for/against in conference games (used for margin tiebreakers)
        public int    ConfPointsFor     { get; set; }
        public int    ConfPointsAgainst { get; set; }

        // Head-to-head results vs other teams in the conference (TeamId → W/L)
        public Dictionary<int, bool> HeadToHeadResults { get; set; } = new();

        // Win pct vs common conference opponents (populated during tiebreaker calc)
        public double CommonOpponentWinPct { get; set; }

        // Combined win pct of all conference opponents (strength of schedule proxy)
        public double ConferenceOpponentWinPct { get; set; }

        // Externally-sourced ranking — CFP, AP, Coaches Poll etc.
        // NULL means unknown / not ranked (stubs out requirements we can't compute)
        public int? CfpRanking      { get; set; }
        public int? ApRanking       { get; set; }
        public int? SportSourceRating { get; set; } // Big 12 / Mountain West / Pac-12 tiebreaker

        public double ConferenceWinPct =>
            (ConferenceWins + ConferenceLosses) > 0
                ? (double)ConferenceWins / (ConferenceWins + ConferenceLosses)
                : 0.0;

        public double OverallWinPct =>
            (OverallWins + OverallLosses) > 0
                ? (double)OverallWins / (OverallWins + OverallLosses)
                : 0.0;
    }
}
