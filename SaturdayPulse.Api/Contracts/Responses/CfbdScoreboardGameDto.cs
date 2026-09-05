using System.Text.Json.Serialization;

namespace SaturdayPulse.Api.Contracts.Responses
{
    /// <summary>
    /// CFBD's /scoreboard?classification=fbs response — one row per game
    /// currently in CFBD's scoreboard window (spans the current and
    /// adjacent days, not scoped by year/week the way /lines is). Used by
    /// GameScorePollingService for the bulk score-only poll. Deliberately
    /// minimal: the real payload also includes venue/weather/betting/
    /// lineScores/winProbability, none of which anything here reads.
    /// </summary>
    public class CfbdScoreboardGameDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; } // "scheduled" | "in_progress" | "completed"

        // UNCONFIRMED — verify against your captured /scoreboard sample before
        // shipping. GraphQL's schema exposes these as currentPeriod/currentClock;
        // REST may or may not carry the "current" prefix. Wrong key name here
        // deserializes to null silently (no exception), same failure shape as
        // the KickoffTime bug — worth actually checking the raw JSON rather
        // than trusting this guess.
        [JsonPropertyName("period")]
        public int? Period { get; set; }

        [JsonPropertyName("clock")]
        public string? Clock { get; set; }

        [JsonPropertyName("homeTeam")]
        public CfbdScoreboardTeamDto? HomeTeam { get; set; }

        [JsonPropertyName("awayTeam")]
        public CfbdScoreboardTeamDto? AwayTeam { get; set; }
    }

    public class CfbdScoreboardTeamDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("points")]
        public int? Points { get; set; }
    }
}