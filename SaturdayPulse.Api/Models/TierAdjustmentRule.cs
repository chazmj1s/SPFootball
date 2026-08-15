namespace SaturdayPulse.Api.Models
{
    public class TierAdjustmentRule
    {
        public string? ConferenceAbbreviation { get; set; }
        public int? TeamId { get; set; }
        public int StartYear { get; set; } = 1965;
        public int EndYear { get; set; } = int.MaxValue; // Defaults to open-ended (forever active)
        public bool IsTier1 { get; set; }
    }
}
