using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SaturdayPulse.Models
{
    /// <summary>
    /// Flattened (Team, Year) -> CoachName lookup, built by iterating every coach in
    /// /coaches?year=T and flattening each coach's Seasons[] array into one row per
    /// (school, year) they coached. Only enough fields to detect year-over-year HC
    /// turnover for the coaching penalty in the Roster Capacity spec — not a full
    /// coaching-history table (win/loss/SRS fields live in the raw CFBD payload only).
    ///
    /// Composite key is (Team, Year). If a mid-season coaching change produces two
    /// season entries for the same (school, year), last one processed wins — this
    /// table only needs to answer "who was HC at end of season Y for team X".
    /// </summary>
    [Table("CoachRecords")]
    [PrimaryKey(nameof(Team), nameof(Year))]
    [Index(nameof(Year))]
    public class CoachRecord
    {
        [Column("Team")]
        public string Team { get; set; } = string.Empty;

        [Column("Year")]
        public int Year { get; set; }

        [Column("CoachName")]
        [MaxLength(150)]
        public string CoachName { get; set; } = string.Empty;
    }
}
