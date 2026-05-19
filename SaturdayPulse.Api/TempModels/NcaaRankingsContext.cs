using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace SaturdayPulse.TempModels;

public partial class NcaaRankingsContext : DbContext
{
    public NcaaRankingsContext()
    {
    }

    public NcaaRankingsContext(DbContextOptions<NcaaRankingsContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Team> Teams { get; set; }

    public virtual DbSet<TeamRecord> TeamRecords { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Team>(entity =>
        {
            entity.ToTable("Team");

            entity.Property(e => e.TeamId).HasColumnName("TeamID");
            entity.Property(e => e.Alias).HasColumnType("varchar(50)");
            entity.Property(e => e.Conference).HasColumnType("varchar(50)");
            entity.Property(e => e.ConferenceAbbr).HasColumnType("varchar(20)");
            entity.Property(e => e.Division).HasColumnType("varchar(20)");
            entity.Property(e => e.TeamName).HasColumnType("varchar(50)");
        });

        modelBuilder.Entity<TeamRecord>(entity =>
        {
            entity.HasIndex(e => e.TeamId, "IX_TeamRecords_TeamID");

            entity.Property(e => e.BaseSos)
                .HasColumnType("REAL (8, 4)")
                .HasColumnName("BaseSOS");
            entity.Property(e => e.CombinedSos)
                .HasColumnType("REAL (8, 4)")
                .HasColumnName("CombinedSOS");
            entity.Property(e => e.Losses).HasColumnType("smallint");
            entity.Property(e => e.PowerRating).HasColumnType("decimal(10,4)");
            entity.Property(e => e.SubSos)
                .HasColumnType("REAL (8, 4)")
                .HasColumnName("SubSOS");
            entity.Property(e => e.TeamId).HasColumnName("TeamID");
            entity.Property(e => e.Wins).HasColumnType("smallint");
            entity.Property(e => e.Year).HasColumnType("smallint");

            entity.HasOne(d => d.Team).WithMany(p => p.TeamRecords).HasForeignKey(d => d.TeamId);
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
