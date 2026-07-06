using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SaturdayPulse.Models
{
    [Table("RecruitPlayers")]
    [Index(nameof(AthleteId), nameof(Year))]
    [Index(nameof(CommittedTo))]
    public class RecruitPlayer
    {
        // CFBD's own recruit id (e.g. "106192") — unique per recruit entry per year,
        // used as PK since AthleteId is null on ~38% of rows and can't anchor a key.
        [Key]
        [Column("Id")]
        [MaxLength(32)]
        public string Id { get; set; } = null!;

        [Column("AthleteId")]
        [MaxLength(32)]
        public string? AthleteId { get; set; }

        [Column("RecruitType")]
        [MaxLength(32)]
        public string RecruitType { get; set; } = null!;

        [Column("Year")]
        public int Year { get; set; }

        [Column("Ranking")]
        public int? Ranking { get; set; }

        [Column("Name")]
        [MaxLength(128)]
        public string Name { get; set; } = null!;

        [Column("School")]
        [MaxLength(128)]
        public string? School { get; set; }

        [Column("CommittedTo")]
        [MaxLength(64)]
        public string? CommittedTo { get; set; }

        [Column("Position")]
        [MaxLength(16)]
        public string Position { get; set; } = null!;

        [Column("Height")]
        public double? Height { get; set; }

        [Column("Weight")]
        public int? Weight { get; set; }

        [Column("Stars")]
        public int Stars { get; set; }

        [Column("Rating")]
        public double Rating { get; set; }

        [Column("City")]
        [MaxLength(64)]
        public string? City { get; set; }

        [Column("StateProvince")]
        [MaxLength(8)]
        public string? StateProvince { get; set; }

        [Column("Country")]
        [MaxLength(8)]
        public string? Country { get; set; }

        // Flattened from CFBD's nested hometownInfo — kept flat rather than an owned
        // type to match RosterPlayer/PlayerStat's flat-table convention.
        [Column("Latitude")]
        public double? Latitude { get; set; }

        [Column("Longitude")]
        public double? Longitude { get; set; }

        [Column("FipsCode")]
        [MaxLength(16)]
        public string? FipsCode { get; set; }
    }
}
