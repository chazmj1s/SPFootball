using Syncfusion.Licensing;

namespace SaturdayPulse.Models;

[Preserve(AllMembers = true)]
public class TeamGameResultView
{
    public int     Week         { get; set; }
    public string? GameDate     { get; set; }
    public string? GameDay      { get; set; }
    public string  Opponent     { get; set; } = string.Empty;
    public int?    OpponentId   { get; set; }
    public string? OpponentConf { get; set; }
    public string? Location     { get; set; }   // "vs" | "@"
    public bool    NeutralSite  { get; set; }
    public string  Result       { get; set; } = string.Empty;  // "W" | "L"
    public string? Score        { get; set; }
    public string? ProjScore    { get; set; }
    public string? Confidence   { get; set; }
    public string? Type         { get; set; }   // "Actual" | "Projected"
    public string? SeasonType   { get; set; }   // "regular" | "postseason"

    public bool IsWin        => Result == "W";
    public bool IsLoss       => Result == "L";
    public bool IsActual     => Type == "Actual";
    public bool IsPostseason => SeasonType == "postseason";

    public string DisplayResult => IsWin ? "W" : IsLoss ? "L" : "–";

    public string OpponentDisplay
    {
        get
        {
            if (NeutralSite)     return $"{Opponent} (N)";
            if (Location == "@") return $"@ {Opponent}";
            return $"vs {Opponent}";
        }
    }

    // Actual score for played games, projected in parens for future games
    public string DisplayScore     => IsActual ? (Score ?? "–") : ProjScore != null ? $" ({ProjScore})" : "–";
    public string DisplayProjScore => ProjScore != null ? $" ({ProjScore})" : "(–)";
}

public class TeamScheduleResponse
{
    public TeamSeasonSummaryView?   Summary { get; set; }
    public List<TeamGameResultView> Games   { get; set; } = new();
}

public class TeamSeasonSummaryView
{
    public int     Year          { get; set; }
    public string  TeamName      { get; set; } = string.Empty;
    public string? Conference    { get; set; }
    public int     Wins          { get; set; }
    public int     Losses        { get; set; }
    public int     PointsFor     { get; set; }
    public int     PointsAgainst { get; set; }
}
