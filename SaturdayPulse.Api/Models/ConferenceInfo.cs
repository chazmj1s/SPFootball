namespace SaturdayPulse.Models
{
    /// <summary>
    /// DTO returned by GET /conferences/{year}.
    /// Carries both Name and Abbreviation so the client picker can display
    /// the full name while storing the abbreviation for filter matching.
    /// </summary>
    public class ConferenceInfo
    {
        public string Name         { get; set; } = string.Empty;  // "Southwest", "Big 12", "Pac-10"
        public string Abbreviation { get; set; } = string.Empty;  // "SWC", "B12", "PAC"
        public string Tier         { get; set; } = string.Empty;  // "P4", "G5"
    }
}
