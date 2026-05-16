using SaturdayPulse.Models;

namespace SaturdayPulse.Repositories.Interfaces
{
    public interface IConferenceRepository
    {
        Task<List<Conference>> GetAllAsync(CancellationToken token = default);
        Task<Conference?> GetByConferenceIdAsync(int conferenceId, CancellationToken token = default);
        Task<Dictionary<int, Conference>> GetDictionaryAsync(CancellationToken token = default);
        Task UpsertAsync(Conference conference, CancellationToken token = default);
        Task UpsertRangeAsync(IEnumerable<Conference> conferences, CancellationToken token = default);
    }
}
