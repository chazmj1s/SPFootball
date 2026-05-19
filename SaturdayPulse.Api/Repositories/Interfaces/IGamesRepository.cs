using SaturdayPulse.Models;

namespace SaturdayPulse.Repositories.Interfaces
{
    public interface IGamesRepository
    {
        Task<List<Games>> GetByYearAsync(int year, CancellationToken token = default);
        Task<List<Games>> GetByYearAndWeekAsync(int year, int week, CancellationToken token = default);
        Task<Games?> GetByGameIdAsync(int gameId, CancellationToken token = default);
        Task UpsertAsync(Games game, CancellationToken token = default);
        Task UpsertRangeAsync(IEnumerable<Games> games, CancellationToken token = default);
        Task<List<Games>> GetRivalryHistoryAsync(int team1Id, int team2Id, int cutoffYear, CancellationToken token = default);
        Task<List<Games>> GetPlayedGamesSinceYearAsync(int fromYear, CancellationToken token = default);
        Task<List<Games>> GetPlayedGamesByYearAndWeekAsync(int year, int week, CancellationToken token = default);
        Task<List<int>> GetPlayedWeeksByYearAsync(int year, CancellationToken token = default);
        Task<List<GameParticipant>> GetGameParticipantsAsync(int year, CancellationToken token = default);
    }
}
