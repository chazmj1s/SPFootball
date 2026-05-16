using System.Text.Json.Serialization;

namespace SaturdayPulse.Contracts
{
    public class CfbdGameV2Dto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("season")]
        public int Season { get; set; }

        [JsonPropertyName("week")]
        public int Week { get; set; }

        [JsonPropertyName("seasonType")]
        public string SeasonType { get; set; } = "regular";

        [JsonPropertyName("startDate")]
        public string? StartDate { get; set; }

        [JsonPropertyName("completed")]
        public bool Completed { get; set; }

        [JsonPropertyName("neutralSite")]
        public bool NeutralSite { get; set; }

        [JsonPropertyName("conferenceGame")]
        public bool ConferenceGame { get; set; }

        [JsonPropertyName("attendance")]
        public int? Attendance { get; set; }

        [JsonPropertyName("venue")]
        public string? Venue { get; set; }

        [JsonPropertyName("homeId")]
        public int HomeId { get; set; }

        [JsonPropertyName("homeTeam")]
        public string HomeTeam { get; set; } = string.Empty;

        [JsonPropertyName("homePoints")]
        public int? HomePoints { get; set; }

        [JsonPropertyName("awayId")]
        public int AwayId { get; set; }

        [JsonPropertyName("awayTeam")]
        public string AwayTeam { get; set; } = string.Empty;

        [JsonPropertyName("awayPoints")]
        public int? AwayPoints { get; set; }
    }
}
