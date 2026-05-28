using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SaturdayPulse.Models
{
    /// <summary>
    /// Represents a single transfer portal entry from CFBD.
    /// Loaded annually for seasons 2021+.
    ///
    /// Net portal strength per team per season is derived from this table:
    ///   Incoming = Destination == team AND Eligibility != Withdrawn
    ///   Outgoing = Origin == team AND Eligibility != Withdrawn AND Destination IS NOT NULL
    ///
    /// Used in InitializeSeasonAsync to adjust week 0 PowerRating before
    /// the season begins. Intended to roll into a Trend-style historical
    /// signal once sufficient years accumulate (2026+).
    /// </summary>
    [Table("PortalEntries")]

    [Index(nameof(Origin))]
    [Index(nameof(Destination))]
    [Index(nameof(Season))]  // worth adding too since you'll query by season often
    public class PortalEntry
    {
        [Key]
        [Column("Id")]
        public int Id { get; set; }

        [Column("Season")]
        public int Season { get; set; }

        [Column("FirstName")]
        public string? FirstName { get; set; }

        [Column("LastName")]
        public string? LastName { get; set; }

        [Column("Position")]
        public string? Position { get; set; }

        [Column("Origin")]
        public string? Origin { get; set; }

        [Column("Destination")]
        public string? Destination { get; set; }

        [Column("TransferDate")]
        public string? TransferDate { get; set; }

        [Column("Rating")]
        public double? Rating { get; set; } = 0;

        [Column("Stars")]
        public int? Stars { get; set; } = 0;

        [Column("Eligibility")]
        public string? Eligibility { get; set; }
    }
}
