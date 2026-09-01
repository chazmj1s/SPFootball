using System.Collections.Generic;

namespace SaturdayPulse.Models
{
    /// <summary>
    /// Represents a team's standing within its conference for championship
    /// qualification purposes.
    ///
    /// NOTE: This is a transient, per-request POCO built fresh from Games /
    /// TeamRecords / WeeklyRankings by BuildConferenceStandingsV2Async /
    /// BuildProjectedConferenceStandingsV2Async in ProductionGameDataService.
    /// It is not an EF entity and has no backing table, so every field here
    /// can be added/changed freely with no migration required.
    /// </summary>
    public class ConferenceStanding
    {
        public int    TeamId          { get; set; }
        public string TeamName        { get; set; }
        public string Conference      { get; set; }
        public string Division        { get; set; }  // null if conference has no divisions
        public int    ConferenceWins { get; set; }
        public int    ConferenceLosses { get; set; }
        public int    ActualConferenceWins { get; set; }
        public int    ActualConferenceLosses { get; set; }
        public int    OverallWins     { get; set; }
        public int    OverallLosses   { get; set; }

        // Season-aggregate points for/against in conference games. Retained
        // for display/legacy purposes — NOT used by the capped-scoring-margin
        // tiebreaker step, which needs per-game data (see ConferenceGameScores)
        // since capping must be applied per game, not to a season total.
        public int    ConfPointsFor     { get; set; }
        public int    ConfPointsAgainst { get; set; }

        /// <summary>
        /// Per-game conference results, needed for the SEC's capped relative
        /// scoring margin tiebreaker step (cap: 42 pts scored / 48 pts allowed,
        /// applied per game before averaging). Populated alongside
        /// ConfPointsFor/ConfPointsAgainst in the same builder loop that
        /// already iterates each team's conference games.
        /// </summary>
        public List<ConferenceGameScore> ConferenceGameScores { get; set; } = new();

        // Head-to-head results vs other teams in the conference (TeamId → W/L).
        // NOTE: a Dictionary<int,bool> cannot represent a split season series
        // (two games vs. the same opponent) — if that ever applies within a
        // single conference-season, this collapses to whichever game was
        // written last. Not currently guarded against.
        public Dictionary<int, bool> HeadToHeadResults { get; set; } = new();

        // Combined win pct of all conference opponents on this team's own
        // schedule (strength-of-schedule proxy). NOT pool-relative — safe to
        // precompute once per team, unlike "common opponents" comparisons
        // (which depend on which other teams are currently tied and so are
        // computed live by the tiebreaker step, not stored here).
        public double ConferenceOpponentWinPct { get; set; }

        /// <summary>
        /// This team's own algorithmic ordinal rank (1 = best), sourced from
        /// GetPowerRankingsV2Async's OverallRank for the relevant year/week.
        /// Replaces every external-ranking tiebreaker step (SportSource,
        /// CFP/AP polls, computer composites) across every conference — per
        /// direction, the app's own rating stands in wherever a conference's
        /// real published rule reaches for a third-party ranking. Null means
        /// not yet computed for this team/year/week (stub, not "worst").
        /// </summary>
        public int? InternalRatingScore { get; set; }

        public double ConferenceWinPct =>
            (ConferenceWins + ConferenceLosses) > 0
                ? (double)ConferenceWins / (ConferenceWins + ConferenceLosses)
                : 0.0;

        public double OverallWinPct =>
            (OverallWins + OverallLosses) > 0
                ? (double)OverallWins / (OverallWins + OverallLosses)
                : 0.0;
    }

    /// <summary>
    /// One conference game's result from a single team's perspective —
    /// opponent, points scored, points allowed. Used only by the capped
    /// scoring margin tiebreaker step.
    /// </summary>
    public class ConferenceGameScore
    {
        public int OpponentId     { get; set; }
        public int PointsFor      { get; set; }
        public int PointsAgainst  { get; set; }
    }
}
