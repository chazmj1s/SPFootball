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

        /// <summary>
        /// Real-or-projected resolved game results — see ResolvedGameResult
        /// remarks. Backed by the ResolvedGameResults DB view; read-only.
        /// </summary>
        IResolvedGameResultRepository ResolvedGameResults { get; }

        // ── Roster Capacity Modifier repositories ─────────────────────────────
        IRosterPlayerRepository RosterPlayers { get; }
        IPlayerStatRepository   PlayerStats   { get; }
        ICoachRecordRepository  CoachRecords  { get; }
        IRecruitPlayerRepository RecruitPlayers { get; }

        /// <summary>
        /// G6/P4 discount coefficients (WinDifferentialDiscount, CaliberGapPoints) —
        /// see TierDiscountCoefficient remarks. Append-only, one or more rows per
        /// season; use GetLatestBySeasonAsync for live consumption.
        /// </summary>
        ITierDiscountCoefficientRepository TierDiscountCoefficients { get; }
        IAnchorBlendCoefficientRepository AnchorBlendCoefficients { get; }  

        // ── User management / entitlement repositories ────────────────────────
        IUserProfileRepository UserProfiles { get; }
        IUserContactInfoRepository UserContactInfo { get; }
        IFollowedTeamRepository FollowedTeams { get; }
        IFollowedGameRepository FollowedGames { get; }
        IUserEntitlementRepository Entitlements { get; }

        // ── Content management ─────────────────────────────────────────────────
        IApplicationContentRepository ApplicationContent { get; }

        // ── Account audit trail ────────────────────────────────────────────────
        IAccountAuditLogRepository AccountAuditLogs { get; }

        Task<int> SaveChangesAsync(CancellationToken token = default);
    }
}
