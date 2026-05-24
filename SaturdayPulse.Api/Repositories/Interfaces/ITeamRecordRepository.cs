using SaturdayPulse.Models;

namespace SaturdayPulse.Repositories.Interfaces
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

        Task<List<TeamRecord>> GetHistoricalAsync(
            int fromYear,
            int toYearExclusive,
            CancellationToken token = default);

        Task<List<TeamRecord>> GetSinceYearWithTeamsAsync(
            int fromYear,
            CancellationToken token = default);

        Task<List<TeamRecord>> GetByTeamAllYearsAsync(
            int teamId,
            CancellationToken token = default);

        Task<List<TeamRecord>> GetRankedByYearAsync(
            int year,
            CancellationToken token = default);

        /// <summary>
        /// Aggregates wins/losses/points from the Game table and upserts into TeamRecords.
        /// Uses an in-database UNION+GroupBy to keep the aggregation server-side.
        /// Optionally scoped to a single year; omit for all years.
        /// </summary>
        Task UpsertFromGamesAsync(int? targetYear = null, CancellationToken token = default);

        Task UpsertFromWeeklyRankingsAsync(int? targetYear = null, CancellationToken token = default);

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
