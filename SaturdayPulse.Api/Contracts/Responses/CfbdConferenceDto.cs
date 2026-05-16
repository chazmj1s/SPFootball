using System.Text.Json.Serialization;

namespace SaturdayPulse.Contracts
{
    public class CfbdConferenceDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("shortName")]
        public string? ShortName { get; set; }

        [JsonPropertyName("abbreviation")]
        public string? Abbreviation { get; set; }

        [JsonPropertyName("classification")]
        public string? Classification { get; set; }
    }
}
