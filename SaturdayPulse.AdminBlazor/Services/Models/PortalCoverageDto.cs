namespace SaturdayPulse.AdminBlazor.Services.Models
{
    /// <summary>Mirrors Api's PortalSeasonCoverage shape.</summary>
    public record PortalSeasonCoverageDto(int Year, int EntryCount);

    /// <summary>Mirrors Api's PortalCoverageResult shape — see GetPortalCoverageAsync.</summary>
    public record PortalCoverageDto(
        string Message,
        List<PortalSeasonCoverageDto> Seasons,
        List<int> MissingSeasons);
}
