using SaturdayPulse.Repositories.Interfaces;

namespace SaturdayPulse.Contracts
{
    public interface IUnitOfWork : IAsyncDisposable
    {
        ITeamRecordRepository TeamRecords { get; }
        ILookupRepository     Lookups     { get; }


        // ── CFBD V2 repositories ──────────────────────────────────────────────
        IConferenceRepository Conferences { get; }
        ITeamsRepository      Teams     { get; }
        IGamesRepository      Games     { get; }
        ILinesRepository      Lines       { get; }
        IProjectionRepository Projections { get; }
        IWeeklyRankingsRepository WeeklyRankings { get; }
        ITeamsConferenceHistoryRepository TeamsConferenceHistory { get; }
        IPortalRepository Portal { get; }

        // ── Roster Capacity Modifier repositories ─────────────────────────────
        IRosterPlayerRepository RosterPlayers { get; }
        IPlayerStatRepository   PlayerStats   { get; }
        ICoachRecordRepository  CoachRecords  { get; }
        IRecruitPlayerRepository RecruitPlayers { get; }

        Task<int> SaveChangesAsync(CancellationToken token = default);
    }
}
