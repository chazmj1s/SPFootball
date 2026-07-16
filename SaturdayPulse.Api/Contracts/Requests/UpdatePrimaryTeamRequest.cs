namespace SaturdayPulse.Contracts.Requests
{
    public class UpdatePrimaryTeamRequest
    {
        public int? TeamId { get; set; } // null clears primary team
    }
}
