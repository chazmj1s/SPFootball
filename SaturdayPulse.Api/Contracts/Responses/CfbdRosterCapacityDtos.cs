using System.Text.Json.Serialization;

namespace SaturdayPulse.Contracts
{
    // ---------------------------------------------------------------------------
    // DTOs matching CFBD's raw JSON shapes for the Roster Capacity Modifier's
    // three new data sources. Verified against real sample payloads (Texas 2025
    // roster, Texas 2025 season stats, 2024-2025 coaches).
    // ---------------------------------------------------------------------------

    public class CfbdRosterEntryDto
    {
        [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("firstName")] public string? FirstName { get; set; }
        [JsonPropertyName("lastName")] public string? LastName { get; set; }
        [JsonPropertyName("team")] public string Team { get; set; } = string.Empty;
        [JsonPropertyName("position")] public string? Position { get; set; }
        [JsonPropertyName("year")] public int? ClassYear { get; set; }
        [JsonPropertyName("recruitIds")] public List<string>? RecruitIds { get; set; }
    }

    public class CfbdPlayerSeasonStatDto
    {
        [JsonPropertyName("season")] public int Season { get; set; }
        [JsonPropertyName("playerId")] public string PlayerId { get; set; } = string.Empty;
        [JsonPropertyName("player")] public string? Player { get; set; }
        [JsonPropertyName("position")] public string? Position { get; set; }
        [JsonPropertyName("team")] public string Team { get; set; } = string.Empty;
        [JsonPropertyName("category")] public string Category { get; set; } = string.Empty;
        [JsonPropertyName("statType")] public string StatType { get; set; } = string.Empty;
        [JsonPropertyName("stat")] public string Stat { get; set; } = string.Empty;
    }

    public class CfbdCoachDto
    {
        [JsonPropertyName("firstName")] public string? FirstName { get; set; }
        [JsonPropertyName("lastName")] public string? LastName { get; set; }
        [JsonPropertyName("seasons")] public List<CfbdCoachSeasonDto> Seasons { get; set; } = new();
    }

    public class CfbdCoachSeasonDto
    {
        [JsonPropertyName("school")] public string School { get; set; } = string.Empty;
        [JsonPropertyName("year")] public int Year { get; set; }
    }

    public class CfbdRecruitPlayerDto
    {
        [JsonPropertyName("id")] public string Id { get; set; }
        [JsonPropertyName("athleteId")] public string? AthleteId { get; set; }
        [JsonPropertyName("recruitType")] public string RecruitType { get; set; }
        [JsonPropertyName("year")] public int Year { get; set; }
        [JsonPropertyName("ranking")] public int? Ranking { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; }
        [JsonPropertyName("school")] public string? School { get; set; }
        [JsonPropertyName("committedTo")] public string? CommittedTo { get; set; }
        [JsonPropertyName("position")] public string Position { get; set; }
        [JsonPropertyName("height")] public double? Height { get; set; }
        [JsonPropertyName("weight")] public int? Weight { get; set; }
        [JsonPropertyName("stars")] public int Stars { get; set; }
        [JsonPropertyName("rating")] public double Rating { get; set; }
        [JsonPropertyName("city")] public string? City { get; set; }
        [JsonPropertyName("stateProvince")] public string? StateProvince { get; set; }
        [JsonPropertyName("country")] public string? Country { get; set; }
        [JsonPropertyName("hometownInfo")] public CfbdHometownInfoDto? HometownInfo { get; set; }
    }

    public class CfbdHometownInfoDto
    {
        [JsonPropertyName("latitude")] public double? Latitude { get; set; }
        [JsonPropertyName("longitude")] public double? Longitude { get; set; }
        [JsonPropertyName("fipsCode")] public string? FipsCode { get; set; }
    }
}
