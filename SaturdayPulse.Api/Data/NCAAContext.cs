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

        // Roster Capacity Modifier tables — PK/index config lives on the entities themselves
        // via [PrimaryKey]/[Index] attributes (see RosterPlayer.cs, PlayerStat.cs, CoachRecord.cs),
        // same pattern as PortalEntry above. Nothing needed in OnModelCreating for these three.
        public DbSet<RosterPlayer> RosterPlayers { get; set; } = null!;
        public DbSet<PlayerStat> PlayerStats { get; set; } = null!;
        public DbSet<CoachRecord> CoachRecords { get; set; } = null!;

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
                .HasIndex(t => new { t.TeamId, t.StartYear })
                .IsUnique()
                .HasDatabaseName("UQ_TeamsConferenceHistory_TeamId_StartYear");
        }
    }
}
