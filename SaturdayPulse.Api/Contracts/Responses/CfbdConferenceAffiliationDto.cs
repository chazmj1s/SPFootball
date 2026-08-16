using System.Text.Json.Serialization;

namespace SaturdayPulse.Api.Contracts.Responses
{
    public class CfbdConferenceAffiliationDto
    {
        [JsonPropertyName("teamId")]
        public int TeamId { get; set; }

        [JsonPropertyName("team")]
        public string Team { get; set; } = string.Empty;

        [JsonPropertyName("conferenceId")]
        public int ConferenceId { get; set; }

        [JsonPropertyName("conference")]
        public string? Conference { get; set; }

        [JsonPropertyName("conferenceAbbreviation")]
        public string? ConferenceAbbreviation { get; set; }

        [JsonPropertyName("classification")]
        public string? Classification { get; set; }

        [JsonPropertyName("conferenceDivision")]
        public string? ConferenceDivision { get; set; }

        [JsonPropertyName("startYear")]
        public int StartYear { get; set; }

        [JsonPropertyName("endYear")]
        public int? EndYear { get; set; }
    }
}
