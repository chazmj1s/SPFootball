namespace SaturdayPulse.ModelViews
{
    /// <summary>
    /// Current + prior season ZRoster and national ordinal rank for one team.
    /// Rank is null when ZRoster hasn't been computed for that team/year — see
    /// RosterCapacityService.GetRosterChangesAsync for the coverage caveat
    /// (ComputeZRosterAsync is a manual admin trigger, not automated).
    /// </summary>
    public class RosterStrengthDto
    {
        public double? CurrentZRoster { get; set; }
        public int? CurrentRank { get; set; }
        public double? PriorZRoster { get; set; }
        public int? PriorRank { get; set; }

        /// <summary>
        /// 10 × Φ(ZRoster) — standard normal CDF applied to the Z-score, then scaled
        /// to a 0–10 display range. Not a linear rescale like the composite metrics
        /// below (ZRoster isn't bounded like Rating is) — this is a real statistical
        /// assumption (roster-talent Z-scores are ~normally distributed across FBS),
        /// not an arbitrary cutoff. Null when the corresponding ZRoster is null.
        /// </summary>
        public double? CurrentRatingDisplay { get; set; }
        public double? PriorRatingDisplay { get; set; }
    }

    /// <summary>
    /// Current/prior value for one of the Recruiting/Portal composite metrics — see
    /// RosterCapacityService.GetRosterChangesAsync for the exact weighted-mean
    /// formula. Null on either side means no usable players existed for that
    /// team/year (e.g. no committed recruits, or no portal activity) — not a
    /// zero-value composite. Portal Out values are pre-negated at the source; this
    /// DTO doesn't apply any sign convention itself.
    /// </summary>
    public class RosterChangeMetricDto
    {
        public double? Current { get; set; }
        public double? Prior { get; set; }
    }

    public class RecruitSummaryDto
    {
        public string Name { get; set; } = string.Empty;
        public string Position { get; set; } = string.Empty;
        public int Stars { get; set; }
        public double Rating { get; set; }
        public int? Ranking { get; set; }
    }

    /// <summary>
    /// One portal transfer, in either direction. OtherTeam is the Origin (for an
    /// outgoing transfer) or Destination (for an incoming one) — whichever is
    /// relevant depends on which list (PortalIn/PortalOut) this row is in.
    /// </summary>
    public class PortalTransferDto
    {
        public string Name { get; set; } = string.Empty;
        public string? Position { get; set; }
        public double? Rating { get; set; }
        public string? OtherTeam { get; set; }
    }

    /// <summary>
    /// One roster player, used for the Retained/Departed/New lists. ClassYear is
    /// the player's eligibility/class year from the roster payload (RosterPlayer.
    /// ClassYear), NOT the season — see RosterPlayer.cs's own remarks on why that
    /// field isn't named "Year".
    /// </summary>
    public class PlayerSummaryDto
    {
        public string Name { get; set; } = string.Empty;
        public string? Position { get; set; }
        public int? ClassYear { get; set; }
    }
}
