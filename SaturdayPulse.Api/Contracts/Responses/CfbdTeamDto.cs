using System.Text.Json.Serialization;

namespace SaturdayPulse.Api.Contracts.Responses
{
    public class CfbdTeamDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("school")]
        public string School { get; set; } = string.Empty;

        [JsonPropertyName("mascot")]
        public string? Mascot { get; set; }

        [JsonPropertyName("abbreviation")]
        public string? Abbreviation { get; set; }

        [JsonPropertyName("conference")]
        public string? Conference { get; set; }

        [JsonPropertyName("division")]
        public string? Division { get; set; }

        [JsonPropertyName("classification")]
        public string? Classification { get; set; }
    }
}