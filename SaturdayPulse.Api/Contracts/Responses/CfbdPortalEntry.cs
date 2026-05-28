namespace SaturdayPulse.Api.Contracts.Responses
{
    public class CfbdPortalEntry
    {
        public int Season { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Position { get; set; }
        public string? Origin { get; set; }
        public string? Destination { get; set; }
        public string? TransferDate { get; set; }
        public double? Rating { get; set; }
        public int? Stars { get; set; }
        public string? Eligibility { get; set; }
    }
}
