using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SaturdayPulse.Models
{
    [Table("TeamRecords")]
    public class TeamRecord
    {
        [Key]
        [Column("Id")]
        public int Id { get; set; }

        [Column("TeamID")]
        public int TeamID { get; set; }

        [Column("Year", TypeName = "smallint")]
        public short Year { get; set; }

        [Column("Wins", TypeName = "smallint")]
        public byte Wins { get; set; }

        [Column("Losses", TypeName = "smallint")]
        public byte Losses { get; set; }

        [Column("PointsFor")]
        public int PointsFor { get; set; }

        [Column("PointsAgainst")]
        public int PointsAgainst { get; set; }

        [Column("BaseSOS", TypeName = "decimal(10,3)")]
        public decimal? BaseSOS { get; set; }

        [Column("SubSOS", TypeName = "decimal(10,3)")]
        public decimal? SubSOS { get; set; }

        [Column("CombinedSOS", TypeName = "decimal(10,4)")]
        public decimal? CombinedSOS { get; set; }

        [Column("PowerRating", TypeName = "decimal(10,4)")]
        public decimal? PowerRating { get; set; }

        [Column("Ranking", TypeName = "decimal(10,4)")]
        public decimal? Ranking { get; set; }

        [ForeignKey("TeamID")]
        public Teams? Teams { get; set; }

        [Column("AvgPointsScored", TypeName = "decimal(5,2)")]
        public decimal AvgPointsScored { get; set; }

        [Column("AvgPointsAllowed", TypeName = "decimal(5,2)")]
        public decimal AvgPointsAllowed { get; set; }

        [Column("OffensiveZScore", TypeName = "decimal(7,4)")]
        public decimal OffensiveZScore { get; set; }

        [Column("DefensiveZScore", TypeName = "decimal(7,4)")]
        public decimal DefensiveZScore { get; set; }

        [Column("OffensiveRank")]
        public int OffensiveRank { get; set; }

        [Column("DefensiveRank")]
        public int DefensiveRank { get; set; }

        [Column("SeedRating", TypeName = "decimal(10,4)")]
        public decimal? SeedRating { get; set; }      // 3-year weighted (50/30/20), normalized PowerRating base

        [Column("TrendRating", TypeName = "decimal(10,4)")]
        public decimal? TrendRating { get; set; }     // 5-year weighted (40/25/15/12/8), normalized PowerRating — pure historical, no ZRoster

        [Column("PedigreeRating", TypeName = "decimal(10,4)")]
        public decimal? PedigreeRating { get; set; }  // 10-year linear decay, normalized PowerRating — pure historical, no ZRoster

        /// <summary>
        /// RETIRED as of the Roster Capacity Modifier rebuild. No longer populated by
        /// ComputePortalMetricsAsync and no longer read by InitializeSeasonAsync — the
        /// (RosterStrength - 1.0) * 0.05 PowerRating bump it used to drive has been
        /// removed. Column left in place (not dropped) per the project's convention of
        /// leaving retired fields intact rather than migrating them out. Do not populate
        /// or consume this going forward; use ZRoster instead.
        /// </summary>
        [Column("RosterStrength", TypeName = "decimal(10,4)")]
        public decimal? RosterStrength { get; set; }

        /// <summary>
        /// RETIRED as of the Roster Capacity Modifier rebuild. Superseded by ZRoster,
        /// which is a richer national-Z-score signal (recruit ratings, transfer ratings,
        /// real prior-year production share for departures, coaching-turnover penalty)
        /// computed by the same ComputePortalMetricsAsync method, in the same column
        /// position in the pipeline. No longer populated or consumed. Left in schema.
        /// </summary>
        [Column("PortalDelta", TypeName = "decimal(10,4)")]
        public decimal? PortalDelta { get; set; }

        /// <summary>
        /// National Z-score of this team's net roster capacity change (weighted inflow
        /// talent minus weighted departed production, position-scarcity adjusted, minus
        /// a 1.5-std-dev coaching-turnover penalty if the HC changed). Computed once per
        /// season by ComputePortalMetricsAsync. Applied directly to PowerRating at
        /// prediction time in GamePredictionService (GetRatingsForWeekAsync), decayed by
        /// week via the Degraded() extension — NOT blended into Seed (reverted; Seed is
        /// pure historical again, same as Trend/Pedigree). Null for years without
        /// roster/recruiting/coaching data loaded.
        /// </summary>
        [Column("ZRoster", TypeName = "decimal(10,4)")]
        public decimal? ZRoster { get; set; }

        /// <summary>
        /// 10 × weighted-mean(Rating, PositionWeight) across this year's committed
        /// recruiting class (RecruitPlayer.CommittedTo == this team). Weighted MEAN,
        /// not sum — bounded 0-10 since Rating is natively 0.0-1.0, so the ×10 is an
        /// exact rescale, not an arbitrary display multiplier. Null if the team has
        /// no committed recruits for the year, not a zero composite. Computed once
        /// per season by ComputeZRosterAsync (folded into the same trigger as
        /// ZRoster — not a separate manual op), so has the same coverage caveat:
        /// only as fresh as the last time that op was run for the year. See
        /// RosterCapacityService.GetRosterChangesAsync / WeightedMeanRatingDisplay.
        /// </summary>
        [Column("RecruitingComposite", TypeName = "decimal(10,4)")]
        public decimal? RecruitingComposite { get; set; }

        /// <summary>
        /// Same formula as RecruitingComposite, over incoming transfer-portal players
        /// (PortalEntry.Destination == this team, Eligibility != Withdrawn). Players
        /// with Rating == 0/null are excluded as "ungraded" rather than dragging the
        /// mean down. Always positive in spirit (a gain) — no sign applied here.
        /// </summary>
        [Column("PortalInComposite", TypeName = "decimal(10,4)")]
        public decimal? PortalInComposite { get; set; }

        /// <summary>
        /// Same formula and ungraded-exclusion rule as PortalInComposite, over
        /// outgoing transfer-portal players (PortalEntry.Origin == this team,
        /// Eligibility != Withdrawn, Destination != null). Pre-negated at compute
        /// time — a loss is always stored as a negative value, so PortalInComposite +
        /// PortalOutComposite is a plain sum (net swing), never a subtraction.
        /// </summary>
        [Column("PortalOutComposite", TypeName = "decimal(10,4)")]
        public decimal? PortalOutComposite { get; set; }

        [NotMapped]
        public List<double>? TrendHistory { get; set; }

        [NotMapped]
        public List<double>? PedigreeHistory { get; set; }

        [NotMapped]
        public int RegularSeasonGames => Year switch
        {
            >= 2006 => 12,
            >= 1980 => 11,
            >= 1965 => 10,
            _ => 12
        };
    }
}
