namespace SaturdayPulse.Models
{
    /// <summary>
    /// Result of GET /api/productiongamedata/playoff-seeding — the projected
    /// 12-team CFP field for a given year/week, off WeeklyRankings' Ranking
    /// field only (no conference championship simulation).
    /// </summary>
    public class PlayoffFieldResult
    {
        public int Year { get; set; }
        public int Week { get; set; }

        /// <summary>The 12 selected teams, ordered by Seed 1-12.</summary>
        public List<PlayoffSeed> Field { get; set; } = new();

        /// <summary>Selection-decision trace — debugging/QA, not for display.</summary>
        public List<string> Log { get; set; } = new();
    }
}
