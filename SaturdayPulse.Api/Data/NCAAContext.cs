using Microsoft.EntityFrameworkCore;
using SaturdayPulse.Models;

namespace SaturdayPulse.Data
{
    public class NCAAContext(DbContextOptions<NCAAContext> options) : DbContext(options)
    {
        // ── Legacy ────────────────────────────────────────────────────────────
        public DbSet<Game>                  Game                  { get; set; }
        public DbSet<Team>                  Team                  { get; set; }
        public DbSet<AvgScoreDelta>         AvgScoreDeltas        { get; set; }
        public DbSet<TeamRecord>            TeamRecords           { get; set; }
        public DbSet<MatchupHistory>        MatchupHistories      { get; set; }
        public DbSet<WeeklyRanking>         WeeklyRankings        { get; set; }
        public DbSet<TeamConferenceHistory> TeamConferenceHistory { get; set; }

        // ── CFBD V2 ───────────────────────────────────────────────────────────
        public DbSet<Conference>             Conferences             { get; set; }
        public DbSet<Teams>                  Teams                   { get; set; }
        public DbSet<Games>                  Games                   { get; set; }
        public DbSet<Lines>                  Lines                   { get; set; }
        public DbSet<TeamsConferenceHistory> TeamsConferenceHistory  { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // ── Legacy ────────────────────────────────────────────────────────

            modelBuilder.Entity<Game>()
                .Property(g => g.Id)
                .ValueGeneratedNever();

            modelBuilder.Entity<Game>().Ignore(e => e.Spread);

            modelBuilder.Entity<MatchupHistory>()
                .HasKey(m => new { m.Team1Id, m.Team2Id });

            modelBuilder.Entity<WeeklyRanking>()
                .HasIndex(w => new { w.TeamID, w.Year, w.Week })
                .IsUnique();

            modelBuilder.Entity<TeamConferenceHistory>(entity =>
            {
                entity.HasIndex(e => new { e.TeamID, e.StartYear })
                      .IsUnique()
                      .HasDatabaseName("IX_TeamConferenceHistory_TeamID_StartYear");

                entity.HasIndex(e => new { e.TeamID, e.EndYear })
                      .HasDatabaseName("IX_TeamConferenceHistory_TeamID_EndYear");
            });

            // ── CFBD V2 ───────────────────────────────────────────────────────

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
                .HasIndex(t => new { t.TeamId, t.StartYear })
                .IsUnique()
                .HasDatabaseName("UQ_TeamsConferenceHistory_TeamId_StartYear");
        }
    }
}
