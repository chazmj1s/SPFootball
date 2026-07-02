using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SaturdayPulse.Models
{
    /// <summary>
    /// One row per player per season pull, from CFBD /teams/roster. Loaded for both
    /// year T and year T-1, so the same PlayerId appears twice (once per Season) —
    /// that's intentional, it's what lets RosterCapacityService diff T vs T-1 rosters
    /// to find retained / departed / inflow players.
    ///
    /// Composite key is (PlayerId, Season), NOT PlayerId alone — PlayerId is a stable
    /// CFBD athlete ID that persists across seasons for a retained player, so it can't
    /// be the sole key if the same player needs a row in both season snapshots.
    /// </summary>
    [Table("RosterPlayers")]
    [PrimaryKey(nameof(PlayerId), nameof(Season))]
    [Index(nameof(Team))]
    [Index(nameof(Season))]
    public class RosterPlayer
    {
        [Column("PlayerId")]
        [MaxLength(20)]
        public string PlayerId { get; set; } = string.Empty;

        [Column("Season")]
        public int Season { get; set; }

        [Column("Team")]
        public string Team { get; set; } = string.Empty;

        [Column("Position")]
        public string? Position { get; set; }

        /// <summary>
        /// Player's eligibility/class year from the roster payload (freshman=1 ... senior/5th=4/5).
        /// Deliberately not named "Year" — that word means Season here, and reusing it for two
        /// different concepts on one entity is a bug waiting to happen.
        /// </summary>
        [Column("ClassYear")]
        public int? ClassYear { get; set; }

        /// <summary>First entry of the roster payload's recruitIds array, if present. Used to
        /// join against /recruiting/players for RecruitRating below.</summary>
        [Column("RecruitId")]
        public string? RecruitId { get; set; }

        /// <summary>
        /// Populated via join against /recruiting/players?year=T using RecruitId. Null until
        /// that join runs, or if the player has no recruiting record (walk-on, JUCO, etc.) —
        /// RosterCapacityService falls back to the 0.70 unrated floor when this is null.
        /// </summary>
        [Column("RecruitRating")]
        public double? RecruitRating { get; set; }

        /// <summary>
        /// Populated from the transfer portal for players who arrived via transfer. Takes
        /// priority over RecruitRating in the inflow talent score (transferRating ?? recruitRating ?? 0.70).
        /// Null for players who did not arrive via the portal.
        /// </summary>
        [Column("TransferRating")]
        public double? TransferRating { get; set; }

        [Column("FirstName")]
        public string? FirstName { get; set; }

        [Column("LastName")]
        public string? LastName { get; set; }
    }
}
