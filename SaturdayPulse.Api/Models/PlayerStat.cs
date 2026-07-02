using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SaturdayPulse.Models
{
    /// <summary>
    /// One row per (player, category, statType) for a given season, sourced from
    /// CFBD /stats/player/season?year=T-1 — a bulk pull, no team filter required.
    ///
    /// StatValue is stored as the raw string CFBD returns rather than pre-parsed to a
    /// number. The categories used by the production-share formula (rushing/receiving
    /// yards, tackles, TFL, sacks) appear to be plain numeric strings, but storing raw
    /// and parsing at compute time in RosterCapacityService keeps ingestion dumb and
    /// avoids silently losing data if an unexpected category/format shows up.
    /// </summary>
    [Table("PlayerStats")]
    [Index(nameof(PlayerId), nameof(Season))]
    [Index(nameof(Team))]
    [Index(nameof(Season))]
    public class PlayerStat
    {
        [Key]
        [Column("Id")]
        public int Id { get; set; }

        /// <summary>CFBD player id — conceptually FK to RosterPlayer.PlayerId, but not a DB-level
        /// FK constraint since PlayerStat is keyed to season T-1 and RosterPlayer may not have a
        /// T-1 row for every player with stats (e.g. players who left the program entirely and
        /// aren't on any current roster pull).</summary>
        [Column("PlayerId")]
        [MaxLength(20)]
        public string PlayerId { get; set; } = string.Empty;

        [Column("Season")]
        public int Season { get; set; }

        [Column("Team")]
        public string Team { get; set; } = string.Empty;

        [Column("Position")]
        public string? Position { get; set; }

        /// <summary>e.g. "rushing", "receiving", "defensive", "punting" — lowercase, as returned by CFBD.</summary>
        [Column("Category")]
        public string Category { get; set; } = string.Empty;

        /// <summary>e.g. "YDS", "TFL" — exact CFBD label, case as returned.</summary>
        [Column("StatType")]
        public string StatType { get; set; } = string.Empty;

        /// <summary>Raw stat value as returned by CFBD, e.g. "823", "44.5". Parsed to numeric
        /// at compute time, not at ingestion time.</summary>
        [Column("StatValue")]
        public string StatValue { get; set; } = string.Empty;
    }
}
