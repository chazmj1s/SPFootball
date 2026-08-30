namespace SaturdayPulse.Api.Contracts.Responses
{
    public class CfbdMatchupResponse
    {
        public string? Team1 { get; set; }
        public string? Team2 { get; set; }
        public int StartYear { get; set; }
        public int EndYear { get; set; }
        public int Team1Wins { get; set; }
        public int Team2Wins { get; set; }
        public int Ties { get; set; }
        public List<CfbdMatchupGame> Games { get; set; } = new();
    }

    public class CfbdMatchupGame
    {
        public int Season { get; set; }
        public int Week { get; set; }
        public string? SeasonType { get; set; }
        public string? Date { get; set; }
        public bool NeutralSite { get; set; }
        public string? Venue { get; set; }
        public string? HomeTeam { get; set; }
        public int? HomeScore { get; set; }
        public string? AwayTeam { get; set; }
        public int? AwayScore { get; set; }
        public string? Winner { get; set; }
    }
}
