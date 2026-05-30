using SaturdayPulse.Models;

namespace SaturdayPulse.Repositories.Interfaces
{
    public interface ITeamsRepository
    {
        Task<List<Teams>> GetAllAsync(CancellationToken token = default);
        Task<Dictionary<int, Teams>> GetByTeamIdsAsync(IEnumerable<int> teamIds, CancellationToken token = default);
        Task<Teams?> GetByNameAsync(string teamName, CancellationToken token = default);
        Task<Teams?> GetByTeamIdAsync(int teamId, CancellationToken token = default);
        Task<Dictionary<int, Teams>> GetDictionaryByTeamIdAsync(CancellationToken token = default);
        Task<Dictionary<string, Teams>> GetDictionaryByNameAsync(CancellationToken token = default);
        Task UpsertAsync(Teams team, CancellationToken token = default);
        Task UpsertRangeAsync(IEnumerable<Teams> teams, CancellationToken token = default);
    }
}
