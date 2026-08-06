namespace SaturdayPulse.Models
{
    /// <summary>
    /// Maps to the ResolvedGameResults DB view — the single source of truth for
    /// "what happened, or is currently projected to happen, in this game."
    ///
    /// Real result (Games.HomePoints/AwayPoints) wins whenever a game has
    /// actually been played; otherwise falls back to that game's Projection row.
    /// Under the current calc design a game has at most one Projection row ever
    /// (keyed at its own native week — see GamePredictionService.BuildProjection
    /// / WeeklyRankingsService remarks), so no "which snapshot" selection logic
    /// is needed here or in any consumer — one row per game, full stop.
    ///
    /// "Played" uses the same convention as everywhere else in the codebase:
    /// HomePoints and AwayPoints both 0 (or null) means unplayed. See the
    /// ResolvedGameResults view definition (migration AddResolvedGameResultsView)
    /// for the exact SQL.
    ///
    /// Read-only. SQLite views can't be written through EF — never Add/Update/
    /// Remove against this type. Query via IResolvedGameResultRepository.
    /// </summary>
    public class ResolvedGameResult
    {
        public int  GameId      { get; set; }
        public int  Year        { get; set; }
        public int  Week        { get; set; }
        public int? HomeId      { get; set; }
        public int? AwayId      { get; set; }
        public int  HomePoints  { get; set; }
        public int  AwayPoints  { get; set; }
        public bool NeutralSite { get; set; }

        /// <summary>True when this row came from Projections rather than a real, played Games row.</summary>
        public bool IsProjected { get; set; }
    }
}
