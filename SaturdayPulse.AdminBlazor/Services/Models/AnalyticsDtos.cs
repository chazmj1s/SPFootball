namespace SaturdayPulse.AdminBlazor.Services.Models;

// Mirrors SaturdayPulse.Api.Contracts.Responses.ProjectionAccuracyResult exactly.
// Property names differ in case only (Mae vs mae) - JsonSerializerDefaults.Web
// gives us case-insensitive matching, so no JsonPropertyName attributes needed.
public record ProjectionAccuracyResultDto(
    int StartYear,
    int EndYear,
    int TotalGames,
    double Mae,
    double TotalMae,
    double WinnerAccuracyPct,
    double SpreadBias,
    double TotalBias,
    double? VegasMae,
    double? VegasWinnerAccuracy,
    int VegasGames,
    double? VegasOpenMae,
    double? VegasOpenWinnerAccuracy,
    int VegasOpenGames,
    double? VegasTotalMae,
    double? VegasTotalBias,
    List<AccuracyByWeekDto> ByWeek,
    List<AccuracyByYearDto> ByYear,
    List<AccuracyByConferenceDto> ByConference,
    List<AccuracyByPhaseDto> ByPhase);

public record AccuracyByWeekDto(int SnapshotWeek, int Games, double Mae, double WinnerAccuracy);

public record AccuracyByYearDto(int Year, int Games, double Mae, double WinnerAccuracy, double SpreadBias);

public record AccuracyByConferenceDto(string Conference, int Games, double Mae, double WinnerAccuracy);

public record AccuracyByPhaseDto(string Phase, int Games, double Mae, double WinnerAccuracy, double? VegasMae);

// Mirrors SaturdayPulse.Api.Contracts.Responses.PortalAccuracyResult exactly.
public record PortalAccuracyResultDto(
    int StartYear,
    int EndYear,
    int TotalGames,
    List<PortalAccuracyByGroupDto> ByPortalGroup,
    double? WinnerEarlyMae,
    double? WinnerLateMae,
    double? WinnerMaeGap,
    double? LoserEarlyMae,
    double? LoserLateMae,
    double? LoserMaeGap);

public record PortalAccuracyByGroupDto(string PortalGroup, string Period, int Games, double Mae, double WinnerAccuracy);
