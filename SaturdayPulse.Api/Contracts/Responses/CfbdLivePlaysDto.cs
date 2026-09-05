using System.Text.Json.Serialization;

namespace SaturdayPulse.Api.Contracts.Responses
{
    /// <summary>
    /// CFBD's /live/plays?gameId=X response — used only for RefreshGameAsync's
    /// score update. Deliberately minimal: the real payload also includes
    /// down/distance/possession/clock and a full drives/plays play-by-play
    /// array, none of which anything currently reads, so those aren't modeled
    /// here. Confirmed 2026-09 against a real in-progress-game response
    /// (Oklahoma vs UTEP, gameId 401856664).
    ///
    /// Single object, NOT a list — unlike CfbdGameLinesWithScoreDto's /lines
    /// response (which is an array filtered by gameId), /live/plays returns
    /// exactly one game object directly. Also returns NO betting-line data at
    /// all — RefreshGameAsync no longer touches Lines as of this change.
    /// </summary>
    public class CfbdLivePlaysDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("teams")]
        public List<CfbdLivePlaysTeamDto> Teams { get; set; } = [];
    }

    public class CfbdLivePlaysTeamDto
    {
        [JsonPropertyName("teamId")]
        public int TeamId { get; set; }

        [JsonPropertyName("team")]
        public string Team { get; set; } = string.Empty;

        [JsonPropertyName("homeAway")]
        public string HomeAway { get; set; } = string.Empty; // "home" | "away"

        [JsonPropertyName("points")]
        public int Points { get; set; }
    }
}