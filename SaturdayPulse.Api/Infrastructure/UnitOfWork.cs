using SaturdayPulse.Contracts;
using SaturdayPulse.Data;
using SaturdayPulse.Repositories;
using SaturdayPulse.Repositories.Implementations;
using SaturdayPulse.Repositories.Interfaces;

namespace SaturdayPulse.Infrastructure
{
    /// <summary>
    /// Wraps the request-scoped NCAAContext. All repositories share the
    /// same instance — no secondary connections, no factory calls mid-pipeline.
    /// </summary>
    public class UnitOfWork : IUnitOfWork
    {
        private readonly NCAAContext _context;

        // ── Legacy repositories ───────────────────────────────────────────────
        public ITeamRecordRepository TeamRecords { get; }
        public ILookupRepository     Lookups     { get; }

        // ── CFBD V2 repositories ──────────────────────────────────────────────
        public IConferenceRepository Conferences { get; }
        public ITeamsRepository      Teams     { get; }
        public IGamesRepository      Games     { get; }
        public ILinesRepository      Lines       { get; }
        public IProjectionRepository Projections { get; }
        public IWeeklyRankingsRepository WeeklyRankings { get; }
        public ITeamsConferenceHistoryRepository TeamsConferenceHistory { get; }

        public UnitOfWork(NCAAContext context)
        {
            _context    = context;

            TeamRecords = new TeamRecordRepository(_context);
            Lookups     = new LookupRepository(_context);
            Conferences = new ConferenceRepository(_context);
            Teams     = new TeamsRepository(_context);
            Games     = new GamesRepository(_context);
            Lines       = new LinesRepository(_context);
            Projections = new ProjectionRepository(_context);
            WeeklyRankings = new WeeklyRankingsRepository(_context);
            TeamsConferenceHistory = new TeamsConferenceHistoryRepository(_context);
        }

        public Task<int> SaveChangesAsync(CancellationToken token = default)
            => _context.SaveChangesAsync(token);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        // Context lifetime is managed by DI (Scoped) — we don't dispose it here.
    }
}
