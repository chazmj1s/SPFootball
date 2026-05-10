using NCAA_Power_Ratings.Models;

namespace NCAA_Power_Ratings.Repositories.Interfaces
{
    public interface IGameRepository
    {
        Task<List<Game>> GetByYearAsync(
            int year,
            CancellationToken token = default);

        Task<List<Game>> GetByYearAndWeekAsync(
            int year,
            int week,
            CancellationToken token = default);

        Task<List<Game>> GetPlayedGamesByYearAsync(
            int year,
            CancellationToken token = default);

        Task<List<Game>> GetPlayedGamesByYearAndWeekAsync(
            int year,
            int week,
            CancellationToken token = default);

        Task<Game?> GetByIdAsync(
            int gameId,
            CancellationToken token = default);

        Task AddRangeAsync(
            IEnumerable<Game> games,
            CancellationToken token = default);

        Task SaveChangesAsync(
            CancellationToken token = default);
    }
}