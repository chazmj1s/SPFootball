using System.Text.Json.Serialization;

namespace SaturdayPulse.Contracts
{
    public class CfbdLinesGameDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("season")]
        public int Season { get; set; }

        [JsonPropertyName("week")]
        public int Week { get; set; }

        [JsonPropertyName("seasonType")]
        public string SeasonType { get; set; } = "regular";

        [JsonPropertyName("homeTeamId")]
        public int HomeTeamId { get; set; }

        [JsonPropertyName("homeTeam")]
        public string HomeTeam { get; set; } = string.Empty;

        [JsonPropertyName("homeScore")]
        public int? HomeScore { get; set; }

        [JsonPropertyName("awayTeamId")]
        public int AwayTeamId { get; set; }

        [JsonPropertyName("awayTeam")]
        public string AwayTeam { get; set; } = string.Empty;

        [JsonPropertyName("awayScore")]
        public int? AwayScore { get; set; }

        [JsonPropertyName("lines")]
        public List<CfbdLineProviderDto> Lines { get; set; } = [];
    }

    public class CfbdLineProviderDto
    {
        [JsonPropertyName("provider")]
        public string Provider { get; set; } = string.Empty;

        [JsonPropertyName("spread")]
        public decimal? Spread { get; set; }

        [JsonPropertyName("spreadOpen")]
        public decimal? SpreadOpen { get; set; }

        [JsonPropertyName("formattedSpread")]
        public string? FormattedSpread { get; set; }

        [JsonPropertyName("overUnder")]
        public decimal? OverUnder { get; set; }

        [JsonPropertyName("overUnderOpen")]
        public decimal? OverUnderOpen { get; set; }

        [JsonPropertyName("homeMoneyline")]
        public int? HomeMoneyline { get; set; }

        [JsonPropertyName("awayMoneyline")]
        public int? AwayMoneyline { get; set; }
    }
}
