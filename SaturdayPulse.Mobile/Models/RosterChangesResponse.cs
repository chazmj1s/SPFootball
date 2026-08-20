namespace SaturdayPulse.Models
{
    /// <summary>
    /// Mobile-side mirror of SaturdayPulse.Contracts.Responses.RosterChangesResult /
    /// SaturdayPulse.ModelViews.*Dto (API). Plain POCOs, deserialized directly via
    /// GetFromJsonAsync — same pattern as TeamSeasonArcResponse, not the two-step
    /// Dto+MappingExtensions pattern used for the main rankings list, since this data
    /// loads once on expand rather than needing INotifyPropertyChanged live updates.
    /// </summary>
    public class RosterChangesResponse
    {
        public int TeamId { get; set; }
        public string? TeamName { get; set; }
        public int Year { get; set; }

        public RosterStrengthInfo? RosterStrength { get; set; }

        public RosterChangeMetric? RecruitingComposite { get; set; }
        public RosterChangeMetric? PortalInComposite { get; set; }
        public RosterChangeMetric? PortalOutComposite { get; set; }
        public RosterChangeMetric? PortalNetComposite { get; set; }

        public List<RecruitSummary> RecruitingClass { get; set; } = new();
        public List<PortalTransfer> PortalIn  { get; set; } = new();
        public List<PortalTransfer> PortalOut { get; set; } = new();

        public List<PlayerSummary> Retained { get; set; } = new();
        public List<PlayerSummary> Departed { get; set; } = new();
        public List<PlayerSummary> New      { get; set; } = new();
    }

    public class RosterStrengthInfo
    {
        public double? CurrentZRoster { get; set; }
        public int?    CurrentRank    { get; set; }
        public double? PriorZRoster   { get; set; }
        public int?    PriorRank      { get; set; }

        /// <summary>10 × Φ(ZRoster) — see RosterCapacityService.Phi on the API side.</summary>
        public double? CurrentRatingDisplay { get; set; }
        public double? PriorRatingDisplay { get; set; }

        /// <summary>
        /// Rank improvement = PriorRank - CurrentRank (positive = moved up, smaller
        /// rank number is better). Null if either side is missing.
        /// </summary>
        public int? RankChange =>
            (CurrentRank.HasValue && PriorRank.HasValue) ? PriorRank - CurrentRank : null;

        public double? RatingChange =>
            (CurrentRatingDisplay.HasValue && PriorRatingDisplay.HasValue)
                ? CurrentRatingDisplay - PriorRatingDisplay
                : null;
    }

    /// <summary>
    /// Current/prior value for one of the Recruiting/Portal composite metrics. Portal
    /// Out values are already negated at the source (API side) — this class doesn't
    /// apply any sign convention itself, just carries whatever it's given.
    /// </summary>
    public class RosterChangeMetric
    {
        public double? Current { get; set; }
        public double? Prior { get; set; }

        public double? Change =>
            (Current.HasValue && Prior.HasValue) ? Current - Prior : null;
    }

    public class RecruitSummary
    {
        public string Name     { get; set; } = string.Empty;
        public string Position { get; set; } = string.Empty;
        public int    Stars    { get; set; }
        public double Rating   { get; set; }
        public int?   Ranking  { get; set; }
    }

    /// <summary>
    /// OtherTeam is Origin for a PortalIn row, Destination for a PortalOut row —
    /// mirrors PortalTransferDto's remarks on the API side.
    /// </summary>
    public class PortalTransfer
    {
        public string  Name      { get; set; } = string.Empty;
        public string? Position  { get; set; }
        public double? Rating    { get; set; }
        public string? OtherTeam { get; set; }
    }

    public class PlayerSummary
    {
        public string  Name      { get; set; } = string.Empty;
        public string? Position  { get; set; }
        public int?    ClassYear { get; set; }
    }
}
