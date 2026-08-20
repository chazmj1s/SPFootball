namespace SaturdayPulse.Models
{
    public class ContenderInfo
    {
        public string TeamName { get; set; }
        public int ConferenceWins { get; set; }
        public int ConferenceLosses { get; set; }
        public int OverallWins { get; set; }
        public int OverallLosses { get; set; }
        public int ActualConferenceWins { get; set; }
        public int ActualConferenceLosses { get; set; }
        public string ConferenceRecord => $"{ConferenceWins}-{ConferenceLosses}";
        public string OverallRecord => $"{OverallWins}-{OverallLosses}";
        public string ActualConferenceRecord => $"{ActualConferenceWins}-{ActualConferenceLosses}";
    }
}
