namespace SaturdayPulse.Models
{
    /// <summary>
    /// Result of projecting the 12-team playoff field for a given year/week,
    /// off WeeklyRankings' Ranking field only.
    /// </summary>
    public class PlayoffFieldResult
    {
        public int Year { get; set; }
        public int Week { get; set; }

        /// <summary>The 12 selected teams, ordered by Seed 1-12.</summary>
        public List<PlayoffSeed> Field { get; set; } = new();

        /// <summary>
        /// Trace of each selection decision — debugging/QA, same spirit as
        /// ConferenceChampionshipService's tiebreaker log. Not for display yet.
        /// </summary>
        public List<string> Log { get; set; } = new();
    }
}
