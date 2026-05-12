using SaturdayPulse.Models;

namespace SaturdayPulse.Repositories.Interfaces
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

        Task<List<Game>> GetByYearUpToWeekAsync(
            int year,
            int maxWeek,
            CancellationToken token = default);

        Task<List<Game>> GetPlayedGamesByYearAsync(
            int year,
            CancellationToken token = default);

        Task<List<Game>> GetPlayedGamesByYearAndWeekAsync(
            int year,
            int week,
            CancellationToken token = default);

        Task<List<Game>> GetPlayedGamesSinceYearAsync(
            int fromYear,
            CancellationToken token = default);

        Task<List<int>> GetPlayedWeeksByYearAsync(
            int year,
            CancellationToken token = default);

        Task<List<Game>> GetRivalryHistoryAsync(
            int team1Id,
            int team2Id,
            int fromYear,
            CancellationToken token = default);

        Task<Game?> GetByIdAsync(
            int gameId,
            CancellationToken token = default);

        Task AddRangeAsync(
            IEnumerable<Game> games,
            CancellationToken token = default);

        /// <summary>
        /// Returns all games for the year expanded into winner and loser perspective rows,
        /// joined with Team for division data. Used by SetSOS and CalculatePowerRatings.
        /// </summary>
        Task<List<GameParticipant>> GetGameParticipantsAsync(
            int year,
            CancellationToken token = default);
    }
}
