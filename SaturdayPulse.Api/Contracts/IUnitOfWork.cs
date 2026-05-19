using SaturdayPulse.Repositories.Interfaces;

namespace SaturdayPulse.Contracts
{
    public interface IUnitOfWork : IAsyncDisposable
    {
        // ── Legacy repositories ───────────────────────────────────────────────
        ITeamRepository       Team       { get; }
        ITeamRecordRepository TeamRecords { get; }
        IGameRepository       Game       { get; }
        ILookupRepository     Lookups     { get; }


        // ── CFBD V2 repositories ──────────────────────────────────────────────
        IConferenceRepository Conferences { get; }
        ITeamsRepository      Teams     { get; }
        IGamesRepository      Games     { get; }
        ILinesRepository      Lines       { get; }
        IProjectionRepository Projections { get; }
        IWeeklyRankingsRepository WeeklyRankings { get; }
        ITeamsConferenceHistoryRepository TeamsConferenceHistory { get; }

        Task<int> SaveChangesAsync(CancellationToken token = default);
    }
}
