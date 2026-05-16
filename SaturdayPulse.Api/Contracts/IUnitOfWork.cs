using SaturdayPulse.Repositories.Interfaces;

namespace SaturdayPulse.Contracts
{
    public interface IUnitOfWork : IAsyncDisposable
    {
        // ── Legacy repositories ───────────────────────────────────────────────
        ITeamRepository       Teams       { get; }
        ITeamRecordRepository TeamRecords { get; }
        IGameRepository       Games       { get; }
        ILookupRepository     Lookups     { get; }

        // ── CFBD V2 repositories ──────────────────────────────────────────────
        IConferenceRepository Conferences { get; }
        ITeamsRepository      TeamsV2     { get; }
        IGamesRepository      GamesV2     { get; }
        ILinesRepository      Lines       { get; }

        Task<int> SaveChangesAsync(CancellationToken token = default);
    }
}
