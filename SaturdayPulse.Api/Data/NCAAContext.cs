using Microsoft.EntityFrameworkCore;
using SaturdayPulse.Models;

namespace SaturdayPulse.Data
{
    public class NCAAContext(DbContextOptions<NCAAContext> options) : DbContext(options)
    {
        public DbSet<AvgScoreDelta>         AvgScoreDeltas        { get; set; }
        public DbSet<AvgScoreDifferential>  AvgScoreDifferentials { get; set; }
        public DbSet<TeamRecord>            TeamRecords           { get; set; }
        public DbSet<MatchupHistory>        MatchupHistories      { get; set; }
        public DbSet<WeeklyRanking>         WeeklyRankings        { get; set; }
        public DbSet<Conference>             Conferences             { get; set; }
        public DbSet<Teams>                  Teams                   { get; set; }
        public DbSet<Games>                  Games                   { get; set; }
        public DbSet<Lines>                  Lines                   { get; set; }
        public DbSet<TeamsConferenceHistory> TeamsConferenceHistory  { get; set; }
        public DbSet<Projection>            Projections             { get; set; } = null!;
        public DbSet<PortalEntry>           PortalEntries           { get; set; }
        public DbSet<TierDiscountCoefficient> TierDiscountCoefficients { get; set; } = null!;
        public DbSet<AnchorBlendCoefficient> AnchorBlendCoefficients { get; set; } = null;


        /// <summary>
        /// Keyless entity backed by the ResolvedGameResults view (migration
        /// AddResolvedGameResultsView) — real result if the game's been played,
        /// otherwise its locked Projection row. Read-only; see
        /// ResolvedGameResult.cs remarks.
        /// </summary>
        public DbSet<ResolvedGameResult>    ResolvedGameResults     { get; set; } = null!;

        // Roster Capacity Modifier tables — PK/index config lives on the entities themselves
        // via [PrimaryKey]/[Index] attributes (see RosterPlayer.cs, PlayerStat.cs, CoachRecord.cs,
        // RecruitPlayer.cs), same pattern as PortalEntry above. Nothing needed in OnModelCreating
        // for these four.
        public DbSet<RosterPlayer> RosterPlayers { get; set; } = null!;
        public DbSet<PlayerStat> PlayerStats { get; set; } = null!;
        public DbSet<CoachRecord> CoachRecords { get; set; } = null!;
        public DbSet<RecruitPlayer> RecruitPlayers { get; set; } = null!;

        // User management / entitlement — added for auth + payment build-out.
        public DbSet<UserProfile> UserProfiles { get; set; } = null!;
        public DbSet<UserContactInfo> UserContactInfos { get; set; } = null!;
        public DbSet<FollowedTeam> FollowedTeams { get; set; } = null!;
        public DbSet<FollowedGame> FollowedGames { get; set; } = null!;

        // Season Pass / entitlement grants — added 2026-07-22, replaces
        // UserProfile.ExpiryDate as the source of truth for IsEntitled.
        // Scoped to CFB only for now (see UserEntitlement.cs class summary).
        public DbSet<UserEntitlement> UserEntitlements { get; set; } = null!;

        // Content management — added for the admin console's Content page.
        // Single-row table; no OnModelCreating config needed (no indexes,
        // no unique constraints — see ApplicationContent.cs class summary).
        public DbSet<ApplicationContent> ApplicationContent { get; set; } = null!;

        // Account audit trail — added for Delete Account. Deliberately no
        // relationship/FK to UserProfile configured anywhere below: these
        // rows must survive DeleteAccountAsync's hard delete of everything
        // else. See AccountAuditLog.cs class summary.
        public DbSet<AccountAuditLog> AccountAuditLogs { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<AvgScoreDifferential>()
                .HasIndex(a => a.StrengthDifferential)
                .IsUnique();

            modelBuilder.Entity<MatchupHistory>()
                .HasKey(m => new { m.Team1Id, m.Team2Id });


            modelBuilder.Entity<Conference>()
                .HasIndex(c => c.ConferenceId)
                .IsUnique()
                .HasDatabaseName("UQ_Conferences_ConferenceId");

            modelBuilder.Entity<Teams>()
                .HasIndex(t => t.TeamId)
                .IsUnique()
                .HasDatabaseName("UQ_Teams_TeamId");

            modelBuilder.Entity<Games>()
                .HasIndex(g => g.GameId)
                .IsUnique()
                .HasDatabaseName("UQ_Games_GameId");

            modelBuilder.Entity<Games>()
                .HasIndex(g => new { g.Year, g.Week })
                .HasDatabaseName("IX_Games_Year_Week");

            modelBuilder.Entity<TeamsConferenceHistory>()
                .HasIndex(t => new { t.TeamId, t.ConferenceId, t.StartYear })
                .IsUnique()
                .HasDatabaseName("UQ_TeamsConferenceHistory_TeamId_ConferenceId_StartYear");

            // --- ResolvedGameResults (view) ---
            // Keyless — a view row has no natural single-column key, and this
            // is read-only anyway (see ResolvedGameResult.cs remarks).
            modelBuilder.Entity<ResolvedGameResult>()
                .HasNoKey()
                .ToView("ResolvedGameResults");

            // --- UserProfile ---
            // Handle is case-insensitive unique — SQLite NOCASE collation
            // applied at the property level, which the index below inherits.
            modelBuilder.Entity<UserProfile>()
                .Property(u => u.Handle)
                .UseCollation("NOCASE");

            modelBuilder.Entity<UserProfile>()
                .HasIndex(u => u.Handle)
                .IsUnique()
                .HasDatabaseName("UQ_UserProfile_Handle");

            // --- UserContactInfo ---
            // 1:1 with UserProfile via shared PK (UserId).
            modelBuilder.Entity<UserContactInfo>()
                .HasOne<UserProfile>()
                .WithOne()
                .HasForeignKey<UserContactInfo>(c => c.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Email is case-insensitive unique — same NOCASE approach as Handle.
            // Charlie@j1stx.com and charlie@j1stx.com are the same account.
            modelBuilder.Entity<UserContactInfo>()
                .Property(c => c.Email)
                .UseCollation("NOCASE");

            modelBuilder.Entity<UserContactInfo>()
                .HasIndex(c => c.Email)
                .IsUnique()
                .HasDatabaseName("UQ_UserContactInfo_Email");

            // --- FollowedTeam / FollowedGame ---
            // Composite PK — a follow either exists or it doesn't per pair.
            modelBuilder.Entity<FollowedTeam>()
                .HasKey(f => new { f.UserId, f.TeamId });

            // FollowedGame follows a matchup (team pair, canonically ordered
            // low/high — enforced in the repository), not a single GameId,
            // so it survives across seasons the same way the old
            // PersonalGameService's rivalry key did.
            modelBuilder.Entity<FollowedGame>()
                .HasKey(f => new { f.UserId, f.Team1Id, f.Team2Id });

            // --- UserEntitlement ---
            // Not unique — a user can accumulate multiple rows over time
            // (renewals, or eventually multiple products), so this is a
            // lookup-performance index, not a constraint.
            modelBuilder.Entity<UserEntitlement>()
                .HasIndex(e => new { e.UserId, e.ProductKey })
                .HasDatabaseName("IX_UserEntitlement_UserId_ProductKey");
        }
    }
}
