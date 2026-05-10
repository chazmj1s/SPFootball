using NCAA_Power_Ratings.Repositories.Interfaces;

namespace NCAA_Power_Ratings.Contracts
{
    /// <summary>
    /// Single shared DbContext for the lifetime of a request.
    /// All repositories read from and write to the same connection.
    /// SaveChangesAsync commits everything tracked so far in one round-trip.
    /// </summary>
    public interface IUnitOfWork : IAsyncDisposable
    {
        ITeamRepository       Teams       { get; }
        ITeamRecordRepository TeamRecords { get; }
        IGameRepository       Games       { get; }
        ILookupRepository     Lookups     { get; }

        Task<int> SaveChangesAsync(CancellationToken token = default);
    }
}
