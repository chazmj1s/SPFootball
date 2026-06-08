namespace SaturdayPulse.Models
{
    /// <summary>
    /// A conference active in a specific year, returned by GET /conferences/{year}.
    /// Both Name and Abbreviation are carried so the picker can display Name
    /// while SelectedConference stores Abbreviation for direct filter matching
    /// against HomeConf/AwayConf on GameResult.
    /// </summary>
    public class ConferenceInfo
    {
        public string Name         { get; set; } = string.Empty;  // "Southwest", "Big 12", "Pac-10"
        public string Abbreviation { get; set; } = string.Empty;  // "SWC", "B12", "PAC"
        public string Tier         { get; set; } = string.Empty;  // "P4", "G5"
    }
}
