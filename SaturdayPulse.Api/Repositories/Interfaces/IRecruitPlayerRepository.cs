using SaturdayPulse.Models;

namespace SaturdayPulse.Repositories.Interfaces
{
    /// <summary>
    /// Repository for CFBD recruiting-class entries, keyed by CFBD's own recruit id
    /// (not AthleteId — ~38% of rows have no AthleteId yet, so it can't anchor a key).
    /// </summary>
    public interface IRecruitPlayerRepository
    {
        /// <summary>
        /// Returns all recruit rows for a given recruiting class year.
        /// </summary>
        Task<List<RecruitPlayer>> GetByYearAsync(int year, CancellationToken token = default);

        /// <summary>
        /// Deletes all recruit rows for the given year and inserts the new batch.
        /// Safe to call multiple times — replaces existing data. Same ExecuteDeleteAsync
        /// pattern as RosterPlayerRepository/CoachRecordRepository.
        /// </summary>
        Task UpsertYearAsync(int year, List<RecruitPlayer> entries, CancellationToken token = default);
    }
}
