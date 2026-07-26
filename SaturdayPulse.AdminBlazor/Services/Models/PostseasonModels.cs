namespace SaturdayPulse.AdminBlazor.Services.Models;

public record PostseasonGameDto(
    int Id,
    string HomeName,
    string AwayName,
    int Week,
    string GameDate,
    string GameDay,
    string SeasonType);

/// <summary>
/// Wraps GET productiongamedata/postseason/v2, which returns { games: [...] }.
/// </summary>
public record PostseasonGamesResponse(List<PostseasonGameDto> Games);

/// <summary>
/// Client-side grouping built from the flat game list - mirrors buildWeekGroups()
/// in postseason.component.ts. Counts are recomputed locally on toggle (refreshCounts())
/// rather than re-fetched, same as the Angular original.
/// </summary>
public class WeekGroup
{
    public int Week { get; init; }
    public List<PostseasonGameDto> Games { get; init; } = new();
    public int BowlCount { get; set; }
    public int PlayoffCount { get; set; }
}
