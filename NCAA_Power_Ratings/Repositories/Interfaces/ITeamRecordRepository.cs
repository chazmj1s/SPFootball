using NCAA_Power_Ratings.Models;

namespace NCAA_Power_Ratings.Repositories.Interfaces
{
    public interface ITeamRecordRepository
    {
        Task<List<TeamRecord>> GetByYearAsync(
            int year,
            CancellationToken token = default);

        Task<TeamRecord?> GetByTeamAndYearAsync(
            int teamId,
            int year,
            CancellationToken token = default);

        Task<Dictionary<int, TeamRecord>> GetByTeamsAndYearAsync(
            IEnumerable<int> teamIds,
            int year,
            CancellationToken token = default);

        Task<List<TeamRecord>> GetByYearWithTeamsAsync(
            int year,
            CancellationToken token = default);

        Task<List<TeamRecord>> GetFbsByYearAsync(
            int year,
            CancellationToken token = default);

        Task<List<TeamRecord>> QueryAsync(
            int? wins = null,
            int? losses = null,
            int? minWins = null,
            int? maxWins = null,
            int? startYear = null,
            int? endYear = null,
            decimal? minPowerRating = null,
            decimal? maxPowerRating = null,
            int limit = 50,
            CancellationToken token = default);
    }
}