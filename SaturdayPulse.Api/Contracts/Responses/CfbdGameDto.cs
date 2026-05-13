using System.Text.Json.Serialization;

namespace SaturdayPulse.Api.Contracts.Responses
{
    public class CfbdGameDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("season")]
        public int Season { get; set; }

        [JsonPropertyName("week")]
        public int Week { get; set; }

        [JsonPropertyName("seasonType")]
        public string SeasonType { get; set; } = string.Empty;

        [JsonPropertyName("startDate")]
        public DateTime? StartDate { get; set; }

        [JsonPropertyName("startTimeTBD")]
        public bool StartTimeTBD { get; set; }

        [JsonPropertyName("completed")]
        public bool Completed { get; set; }

        [JsonPropertyName("neutralSite")]
        public bool NeutralSite { get; set; }

        [JsonPropertyName("conferenceGame")]
        public bool ConferenceGame { get; set; }

        [JsonPropertyName("venueId")]
        public int? VenueId { get; set; }

        [JsonPropertyName("venue")]
        public string? Venue { get; set; }

        [JsonPropertyName("homeId")]
        public int HomeId { get; set; }

        [JsonPropertyName("homeTeam")]
        public string HomeTeam { get; set; } = string.Empty;

        [JsonPropertyName("homeConference")]
        public string? HomeConference { get; set; }

        [JsonPropertyName("homeClassification")]
        public string? HomeClassification { get; set; }

        [JsonPropertyName("homePoints")]
        public int? HomePoints { get; set; }

        [JsonPropertyName("awayId")]
        public int AwayId { get; set; }

        [JsonPropertyName("awayTeam")]
        public string AwayTeam { get; set; } = string.Empty;

        [JsonPropertyName("awayConference")]
        public string? AwayConference { get; set; }

        [JsonPropertyName("awayClassification")]
        public string? AwayClassification { get; set; }

        [JsonPropertyName("awayPoints")]
        public int? AwayPoints { get; set; }
    }
}