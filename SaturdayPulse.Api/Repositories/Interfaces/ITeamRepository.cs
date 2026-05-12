using SaturdayPulse.Models;

namespace SaturdayPulse.Repositories.Interfaces
{
    public interface ITeamRepository
    {
        Task<Team?> GetByIdAsync(
            int teamId,
            CancellationToken token = default);

        Task<Team?> GetByNameAsync(
            string teamName,
            CancellationToken token = default);

        Task<List<Team>> GetAllAsync(
            CancellationToken token = default);

        Task<List<Team>> GetFbsTeamsAsync(
            CancellationToken token = default);

        Task<Dictionary<int, Team>> GetTeamDictionaryAsync(
            CancellationToken token = default);

        Task<Dictionary<string, Team>> GetTeamDictionaryByNameAsync(
            CancellationToken token = default);
    }
}
