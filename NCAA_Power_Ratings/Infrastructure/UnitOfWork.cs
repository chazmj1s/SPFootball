using NCAA_Power_Ratings.Contracts;
using NCAA_Power_Ratings.Data;
using NCAA_Power_Ratings.Repositories.Implementations;
using NCAA_Power_Ratings.Repositories.Interfaces;

namespace NCAA_Power_Ratings.Infrastructure
{
    /// <summary>
    /// Wraps the request-scoped NCAAContext. All four repositories share the
    /// same instance — no secondary connections, no factory calls mid-pipeline.
    /// </summary>
    public class UnitOfWork : IUnitOfWork
    {
        private readonly NCAAContext _context;

        public ITeamRepository       Teams       { get; }
        public ITeamRecordRepository TeamRecords { get; }
        public IGameRepository       Games       { get; }
        public ILookupRepository     Lookups     { get; }

        public UnitOfWork(NCAAContext context)
        {
            _context    = context;
            Teams       = new TeamRepository(_context);
            TeamRecords = new TeamRecordRepository(_context);
            Games       = new GameRepository(_context);
            Lookups     = new LookupRepository(_context);
        }

        public Task<int> SaveChangesAsync(CancellationToken token = default)
            => _context.SaveChangesAsync(token);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        // Context lifetime is managed by DI (Scoped) — we don't dispose it here.
    }
}
