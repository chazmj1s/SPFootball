// ═══════════════════════════════════════════════════════════════════════════
// Models/TeamTrendData.cs
//
// DTOs for GET /api/productiongamedata/rollingAverages/team?teamId=X&startYear=Y
// Response JSON maps directly to these classes via GetFromJsonAsync<TeamTrendData>.
// ═══════════════════════════════════════════════════════════════════════════

namespace NCAA_Power_Ratings.Mobile.Models
{
    /// <summary>
    /// Top-level response from /rollingAverages/team.
    /// One TeamTrendData per team; History has one entry per season.
    /// </summary>
    public class TeamTrendData
    {
        public int TeamId { get; set; }
        public string TeamName { get; set; } = string.Empty;
        public string? Conference { get; set; }
        public List<TeamYearHistory> History { get; set; } = new();
    }

    /// <summary>
    /// One season's Seed / Trend / Pedigree data for a team.
    /// TrendHistory and PedigreeHistory are per-game rolling arrays
    /// that feed directly into the Syncfusion SplineAreaSeries charts.
    /// </summary>
    public class TeamYearHistory
    {
        public int Year { get; set; }
        public int Wins { get; set; }
        public int Losses { get; set; }
        public double SeedRating { get; set; }
        public double TrendRating { get; set; }
        /// <summary>Per-game rolling trend values. e.g. [1.0208, 1, 0.6667, 0.4167, 0.7]</summary>
        public List<double> TrendHistory { get; set; } = new();
        public double PedigreeRating { get; set; }
        /// <summary>Per-game pedigree values (may include bowl/playoff games).</summary>
        public List<double> PedigreeHistory { get; set; } = new();
    }
}
