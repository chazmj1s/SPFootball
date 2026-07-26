namespace SaturdayPulse.AdminBlazor.Services.Models;

/// <summary>
/// Shape of GET /api/productiongamedata/diagnostic, as consumed by dashboard.component.html.
/// </summary>
public class DiagnosticDto
{
    public string Database { get; set; } = string.Empty;
    public int TotalTeams { get; set; }
    public int TotalGames { get; set; }
    public int TotalRecords { get; set; }
    public int RecordsWithPowerRating { get; set; }
    public List<int> YearsWithData { get; set; } = new();
}
