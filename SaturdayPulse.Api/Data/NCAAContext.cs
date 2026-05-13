using Microsoft.EntityFrameworkCore;
using SaturdayPulse.Models;

namespace SaturdayPulse.Data
{
    public class NCAAContext(DbContextOptions<NCAAContext> options) : DbContext(options)
    {
        public DbSet<Game> Game { get; set; }
        public DbSet<Team> Team { get; set; }
        public DbSet<AvgScoreDelta> AvgScoreDeltas { get; set; }
        public DbSet<TeamRecord> TeamRecords { get; set; }
        public DbSet<MatchupHistory> MatchupHistories { get; set; }
        public DbSet<WeeklyRanking> WeeklyRankings { get; set; }
        public DbSet<TeamConferenceHistory> TeamConferenceHistory { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Game>()
                .Property(g => g.Id)
                .ValueGeneratedNever();

            modelBuilder.Entity<Game>().Ignore(e => e.Spread);

            // Composite key for MatchupHistory
            modelBuilder.Entity<MatchupHistory>()
                .HasKey(m => new { m.Team1Id, m.Team2Id });

            // Unique index on WeeklyRanking: one row per team per year per week
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
        }
    }
}
