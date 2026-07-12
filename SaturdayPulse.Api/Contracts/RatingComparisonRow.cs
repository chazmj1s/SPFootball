namespace SaturdayPulse.Contracts
{
    /// <summary>
    /// One row per real game, comparing the production snapshot-cliff prediction
    /// against the experimental K=4 inertia-blended prediction for the same matchup.
    /// Read-only diagnostic output — never persisted.
    ///
    /// NEW FILE — part of the K=4 inertia-blending experimental comparison path.
    /// </summary>
    public record RatingComparisonRow(
        int Year,
        int Week,
        int GameId,
        int HomeTeamId,
        string HomeTeamName,
        int AwayTeamId,
        string AwayTeamName,
        int HomeGamesPlayed,
        int AwayGamesPlayed,
        decimal? ProductionSpread,      // home-team-perspective spread, production method
        decimal? ExperimentalSpread,    // home-team-perspective spread, K=4 blend
        decimal? SpreadDelta,           // Experimental - Production
        decimal? ProductionTotal,       // predicted O/U, production method
        decimal? ExperimentalTotal,     // predicted O/U, K=4 blend
        decimal? TotalDelta);           // Experimental - Production
}
