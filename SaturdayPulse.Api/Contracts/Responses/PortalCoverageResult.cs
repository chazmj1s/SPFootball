using System.Collections.Generic;

namespace SaturdayPulse.Api.Contracts.Responses
{
    /// <summary>
    /// One season's portal-data coverage — entry count, or 0 if nothing has been
    /// loaded for that season yet.
    /// </summary>
    public record PortalSeasonCoverage(int Year, int EntryCount);

    /// <summary>
    /// Result of GetPortalCoverageAsync — a season-by-season count of PortalEntries
    /// rows from the first year portal data exists (2021) through the current year,
    /// plus the list of seasons with zero entries.
    /// </summary>
    public record PortalCoverageResult(
        string Message,
        List<PortalSeasonCoverage> Seasons,
        List<int> MissingSeasons);
}
