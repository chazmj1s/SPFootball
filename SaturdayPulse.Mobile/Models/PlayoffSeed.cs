namespace SaturdayPulse.Models
{
    /// <summary>
    /// One team's entry in the projected 12-team playoff field.
    /// Mirrors the Api-side PlayoffSeed DTO field-for-field so
    /// HttpClient.GetFromJsonAsync deserializes it directly (same convention
    /// as TeamSeasonArcResponse / RosterChangesResponse).
    /// </summary>
    public class PlayoffSeed
    {
        public int     TeamID         { get; set; }
        public string  TeamName       { get; set; } = "";
        public string? ConferenceAbbr { get; set; }
        public int     Seed           { get; set; }
        public double? Ranking        { get; set; }
        public int     Wins           { get; set; }
        public int     Losses         { get; set; }
        public double? CombinedSOS    { get; set; }

        /// <summary>
        /// "ACC" / "B1G" / "B12" / "SEC" (P4 auto bid), "Ind" (Notre Dame top-12
        /// auto bid), "G6" (highest-ranked Group of Six team), or null for an
        /// at-large (next-highest Ranking) spot.
        /// </summary>
        public string? AutoBidReason  { get; set; }

        public bool    IsAutoBid      => AutoBidReason != null;

        /// <summary>Seeds 1-4 — top four overall, first-round bye.</summary>
        public bool    HasBye         => Seed is >= 1 and <= 4;
    }
}
