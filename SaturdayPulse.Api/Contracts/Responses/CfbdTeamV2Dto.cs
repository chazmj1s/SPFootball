using System.Text.Json.Serialization;

namespace SaturdayPulse.Contracts
{
    public class CfbdTeamV2Dto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("school")]
        public string School { get; set; } = string.Empty;

        [JsonPropertyName("mascot")]
        public string? Mascot { get; set; }

        [JsonPropertyName("abbreviation")]
        public string? Abbreviation { get; set; }

        [JsonPropertyName("alternateNames")]
        public List<string>? AlternateNames { get; set; }

        [JsonPropertyName("conference")]
        public string? Conference { get; set; }

        [JsonPropertyName("classification")]
        public string? Classification { get; set; }
    }
}
