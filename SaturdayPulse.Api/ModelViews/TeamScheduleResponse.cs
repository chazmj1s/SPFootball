using SaturdayPulse.ModelViews;

namespace SaturdayPulse.ModelViews
{
    public class TeamScheduleResponse
    {
        public TeamSeasonSummaryView? Summary { get; set; }
        public List<TeamGameResultView> Games { get; set; } = new();
    }
}