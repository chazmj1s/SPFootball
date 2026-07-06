using SaturdayPulse.Models;

namespace SaturdayPulse.Repositories.Interfaces
{
    /// <summary>
    /// Repository for flattened (Team, Year) coach records, used to detect head-coach
    /// turnover for the coaching-penalty step in the Roster Capacity Modifier.
    /// </summary>
    public interface ICoachRecordRepository
    {
        /// <summary>
        /// Returns all coach records for a given year (across all teams).
        /// </summary>
        Task<List<CoachRecord>> GetByYearAsync(int year, CancellationToken token = default);

        /// <summary>
        /// Returns, for every team with a coach record in `year`, whether the head
        /// coach changed versus year-1. Comparison is whitespace/casing-normalized —
        /// raw CoachName strings from CFBD can drift between season pulls (extra
        /// spaces, casing) without representing an actual coaching change, and an
        /// exact-string comparison was confirmed producing false positives (e.g.
        /// flagging real, unchanged coaches at teams that kept the same HC). A team
        /// missing a prior-year record returns false (can't confirm a change, so
        /// don't penalize) rather than treating missing data as a change.
        /// </summary>
        Task<IReadOnlyDictionary<string, bool>> GetCoachChangeByTeamAsync(
            int year, CancellationToken token = default);

        /// <summary>
        /// Deletes all coach records for the given year and inserts the new batch.
        /// Safe to call multiple times — replaces existing data.
        /// </summary>
        Task UpsertYearAsync(int year, List<CoachRecord> entries, CancellationToken token = default);
    }
}
