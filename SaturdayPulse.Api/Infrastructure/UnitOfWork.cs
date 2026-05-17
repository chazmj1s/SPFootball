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
        public ITeamRepository       Team       { get; }
        public ITeamRecordRepository TeamRecords { get; }
        public IGameRepository       Game       { get; }
        public ILookupRepository     Lookups     { get; }

        // ── CFBD V2 repositories ──────────────────────────────────────────────
        public IConferenceRepository Conferences { get; }
        public ITeamsRepository      TeamsV2     { get; }
        public IGamesRepository      GamesV2     { get; }
        public ILinesRepository      Lines       { get; }
        public IProjectionRepository Projections { get; }
        public IWeeklyRankingsRepository WeeklyRankings { get; }
        public ITeamsConferenceHistoryRepository TeamsConferenceHistory { get; }

        public UnitOfWork(NCAAContext context)
        {
            _context    = context;

            // Legacy
            Team       = new TeamRepository(_context);
            TeamRecords = new TeamRecordRepository(_context);
            Game       = new GameRepository(_context);
            Lookups     = new LookupRepository(_context);

            // CFBD V2
            Conferences = new ConferenceRepository(_context);
            TeamsV2     = new TeamsRepository(_context);
            GamesV2     = new GamesRepository(_context);
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
