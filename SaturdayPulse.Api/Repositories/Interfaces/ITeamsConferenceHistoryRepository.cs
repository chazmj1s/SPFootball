using SaturdayPulse.Models;

public interface ITeamsConferenceHistoryRepository
{
    Task<List<TeamsConferenceHistory>> GetByTeamIdAsync(int teamId, CancellationToken token = default);
    Task<TeamsConferenceHistory?> GetByTeamAndYearAsync(int teamId, int year, CancellationToken token = default);
    Task<Dictionary<int, int>> GetConferenceIdsByYearAsync(int year, CancellationToken token = default);
    Task AddAsync(TeamsConferenceHistory record, CancellationToken token = default);
    Task UpdateAsync(TeamsConferenceHistory record, CancellationToken token = default);
    Task ClearFromStartYearAsync(int startYear, CancellationToken token = default);
    Task AddRangeAsync(IEnumerable<TeamsConferenceHistory> records, CancellationToken token = default);
}
