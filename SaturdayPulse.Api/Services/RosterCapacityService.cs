using SaturdayPulse.Contracts;
using SaturdayPulse.Extensions;
using SaturdayPulse.Models;

namespace SaturdayPulse.Services
{
    /// <summary>
    /// Computes ZRoster — a national Z-score of net roster-composition change for a
    /// given season — and persists it to TeamRecords.ZRoster. Formerly lived inside
    /// PortalRepository as ComputePortalMetricsAsync (which computed the now-retired
    /// RosterStrength/PortalDelta pair); moved here because this is real calculation
    /// logic (positional weighting, production-share formulas, set reconciliation,
    /// national z-scoring, coaching-turnover penalty), not data access.
    ///
    /// Goes through IUnitOfWork (RosterPlayers, PlayerStats, CoachRecords, Teams,
    /// TeamRecords) rather than a raw DbContext — matching RollingAverageService's
    /// own constructor convention.
    ///
    /// ZRoster calculation:
    ///   raw score = Σ(inflow talent × positional weight) − Σ(departed production × positional weight)
    ///   inflow talent  = RosterPlayer.TransferRating ?? RecruitRating ?? 0.70 floor
    ///   departed cost  = prior-year production share (from PlayerStat) if available,
    ///                    else the SAME fallback cascade used for inflow — this is how
    ///                    the O-line gap (no box-score stats exist for linemen) is
    ///                    handled, no separate flat-penalty mechanism.
    ///   raw score is z-scored against the FBS-wide mean/stddev for the season, then
    ///   adjusted by a coaching-turnover penalty (-1.5 std devs) if the team's HC
    ///   changed year over year.
    ///
    /// Set reconciliation (retained/departed/inflow) diffs RosterPlayer.PlayerId
    /// between the current season and season-1, matching the existing composite-key
    /// convention (PlayerId, Season, Team).
    ///
    /// ASSUMPTION FLAGGED: assumes RosterPlayer.TransferRating has already been
    /// populated by some existing portal-application method (mirroring
    /// ApplyRecruitRatingsAsync's pattern for RecruitRating). That method wasn't
    /// available to review — if it doesn't exist or hasn't run, TransferRating will
    /// always be null and every inflow player silently falls back to
    /// RecruitRating/0.70. Worth confirming before trusting real output.
    ///
    /// Only FBS teams are included — matched by team name via the roster's own Team
    /// field, consistent with the existing FBS-name-matching convention used
    /// elsewhere in this pipeline. IRosterPlayerRepository/IPlayerStatRepository
    /// don't offer an FBS-filtered query, so filtering happens client-side after
    /// GetBySeasonAsync — same two-step pattern (season filter in the query, FBS-name
    /// dictionary lookup afterward) required to avoid the earlier EF Core
    /// Dictionary.ContainsKey translation failure.
    /// </summary>
    public class RosterCapacityService
    {
        private readonly IUnitOfWork _uow;

        // Position tier weights — QB touches every play, trenches decide games.
        // Carried over unchanged from PortalRepository — left as-is per the call to
        // revisit once real ZRoster numbers are visible, rather than collapsing to
        // the five-group scheme from the original spec doc up front.
        private static readonly Dictionary<string, double> PositionWeights = new(StringComparer.OrdinalIgnoreCase)
        {
            ["QB"]   = 2.5,
            ["OT"]   = 2.0,
            ["IOL"]  = 2.0,
            ["OL"]   = 2.0,
            ["DE"]   = 2.0,
            ["DL"]   = 2.0,
            ["EDGE"] = 2.0,
            ["CB"]   = 1.5,
            ["WR"]   = 1.5,
            ["LB"]   = 1.5,
            ["RB"]   = 1.5,
            ["S"]    = 1.0,
            ["TE"]   = 1.0,
            ["K"]    = 1.0,
            ["P"]    = 1.0,
            ["LS"]   = 1.0,
        };

        // Coaching-turnover penalty, subtracted from ZRoster (post-national-Z-score)
        // when a team's head coach changed year over year. Per the original spec.
        private const double CoachingTurnoverPenalty = 1.5;

        // Unrated-player floor — used whenever a player has no transfer rating, no
        // recruit rating, and (for departures) no measurable prior-year production.
        private const double UnratedFloor = 0.70;

        public RosterCapacityService(IUnitOfWork uow) => _uow = uow;

        /// <summary>
        /// Computes and persists ZRoster for all FBS teams for the given season.
        /// Returns count of teams updated.
        /// </summary>
        public async Task<int> ComputeZRosterAsync(int season, CancellationToken token = default)
        {
            var priorSeason = season - 1;

            var allTeams = await _uow.Teams.GetAllAsync(token);
            var fbsTeams = allTeams
                .Where(t => string.Equals(t.Division, "fbs", StringComparison.OrdinalIgnoreCase))
                .ToList();
            var fbsNameToId = fbsTeams
                .ToDictionary(t => t.TeamName, t => t.TeamId, StringComparer.OrdinalIgnoreCase);

            // GetBySeasonAsync returns all teams for the season — no FBS filter
            // available at the repo level, so it's applied client-side afterward.
            var currentRosterAll = await _uow.RosterPlayers.GetBySeasonAsync(season, token);
            var currentRoster = currentRosterAll
                .Where(r => fbsNameToId.ContainsKey(r.Team))
                .ToList();

            var priorRosterAll = await _uow.RosterPlayers.GetBySeasonAsync(priorSeason, token);
            var priorRoster = priorRosterAll
                .Where(r => fbsNameToId.ContainsKey(r.Team))
                .ToList();

            if (currentRoster.Count == 0) return 0;

            // Prior-year production — used only to score departures.
            var priorStatsAll = await _uow.PlayerStats.GetBySeasonAsync(priorSeason, token);
            var priorStats = priorStatsAll
                .Where(s => fbsNameToId.ContainsKey(s.Team))
                .ToList();

            var coachChangedByTeam = await _uow.CoachRecords.GetCoachChangeByTeamAsync(season, token);

            // ── Production-share denominators, built once from prior-year stats ────
            // Simplified proxy per the original spec: offensive skill positions share
            // team total yards; defensive positions share a weighted tackles/TFL/sacks
            // pool. Anyone without a matching stat row (O-line, or genuinely no
            // recorded stats) falls through to DepartureCost's fallback below.
            var statsByTeamAndPlayer = priorStats
                .GroupBy(s => (s.Team, s.PlayerId))
                .ToDictionary(g => g.Key, g => g.ToList());

            var teamYardsTotal = priorStats
                .Where(s => s.StatType == "YDS")
                .GroupBy(s => s.Team, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Sum(s => s.StatValue.ToDecimal()), StringComparer.OrdinalIgnoreCase);

            var teamDefWeightedTotal = priorStats
                .Where(s => s.Category == "defensive" &&
                            (s.StatType == "TOT" || s.StatType == "TFL" || s.StatType == "SACKS"))
                .GroupBy(s => s.Team, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => g.Sum(s => WeightedDefensiveValue(s.StatType, (double)s.StatValue.ToDecimal())),
                    StringComparer.OrdinalIgnoreCase);

            double ProductionShare(RosterPlayer departed)
            {
                if (!statsByTeamAndPlayer.TryGetValue((departed.Team, departed.PlayerId), out var rows))
                    return -1; // sentinel — no stat rows at all, caller falls back

                var yards = rows.Where(r => r.StatType == "YDS").Sum(r => r.StatValue.ToDecimal());
                if (yards > 0 && teamYardsTotal.TryGetValue(departed.Team, out var teamYards) && teamYards > 0)
                    return (double)(yards / teamYards);

                var defWeighted = rows.Sum(r => WeightedDefensiveValue(r.StatType, (double)r.StatValue.ToDecimal()));
                if (defWeighted > 0 && teamDefWeightedTotal.TryGetValue(departed.Team, out var teamDef) && teamDef > 0)
                    return defWeighted / teamDef;

                return -1; // had stat rows but none matched either bucket (e.g. kicker) — fall back
            }

            double InflowTalent(RosterPlayer arrived)
                => arrived.TransferRating ?? arrived.RecruitRating ?? UnratedFloor;

            double DepartureCost(RosterPlayer departed)
            {
                var share = ProductionShare(departed);
                if (share >= 0) return share;

                // No measurable production (O-line, or just no stats) — fall back to
                // the same talent-rating cascade used for inflow. This is necessarily
                // a stale number for veterans (their recruit rating may be 3-4 years
                // old), but it's the only signal available with no box-score history.
                return departed.TransferRating ?? departed.RecruitRating ?? UnratedFloor;
            }

            // ── Set reconciliation + raw score, per team ────────────────────────────
            var currentByTeam = currentRoster
                .GroupBy(r => r.Team, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
            var priorByTeam = priorRoster
                .GroupBy(r => r.Team, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            var rawScoreByTeam = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            foreach (var (team, currentTeamRoster) in currentByTeam)
            {
                var currentIds = currentTeamRoster.Select(p => p.PlayerId).ToHashSet();
                var priorTeamRoster = priorByTeam.TryGetValue(team, out var pr) ? pr : [];
                var priorIds = priorTeamRoster.Select(p => p.PlayerId).ToHashSet();

                var inflow   = currentTeamRoster.Where(p => !priorIds.Contains(p.PlayerId));
                var departed = priorTeamRoster.Where(p => !currentIds.Contains(p.PlayerId));

                double inflowScore = inflow.Sum(p =>
                    InflowTalent(p) * PositionWeights.GetValueOrDefault(p.Position ?? "", 1.0));

                double departedScore = departed.Sum(p =>
                    DepartureCost(p) * PositionWeights.GetValueOrDefault(p.Position ?? "", 1.0));

                rawScoreByTeam[team] = inflowScore - departedScore;
            }

            if (rawScoreByTeam.Count == 0) return 0;

            // ── National Z-score across FBS teams ───────────────────────────────────
            var mean = rawScoreByTeam.Values.Average();
            var stdDev = rawScoreByTeam.Count > 1
                ? Math.Sqrt(rawScoreByTeam.Values.Average(v => Math.Pow(v - mean, 2)))
                : 0.0;

            var zRosterByTeam = rawScoreByTeam.ToDictionary(
                kvp => kvp.Key,
                kvp => stdDev > 0 ? Math.Round((kvp.Value - mean) / stdDev, 4) : 0.0,
                StringComparer.OrdinalIgnoreCase);

            // ── Coaching turnover penalty ────────────────────────────────────────────
            foreach (var team in zRosterByTeam.Keys.ToList())
            {
                if (coachChangedByTeam.TryGetValue(team, out var changed) && changed)
                    zRosterByTeam[team] -= CoachingTurnoverPenalty;
            }

            // ── Persist to TeamRecords ───────────────────────────────────────────────
            var teamRecords = await _uow.TeamRecords.GetByYearAsync(season, token);

            int updated = 0;
            foreach (var record in teamRecords)
            {
                var team = fbsTeams.FirstOrDefault(t => t.TeamId == record.TeamID);
                if (team == null) continue;

                record.ZRoster = zRosterByTeam.TryGetValue(team.TeamName, out var z)
                    ? (decimal)z : null;

                updated++;
            }

            await _uow.SaveChangesAsync(token);

            return updated;
        }

        /// <summary>
        /// Runs ComputeZRosterAsync for every season that has portal data loaded.
        /// NOTE: still driven off IPortalRepository.GetDistinctSeasonsAsync (i.e.
        /// PortalEntries) rather than RosterPlayers/PlayerStats/RecruitPlayers — a
        /// known, flagged mismatch, since those are the tables ZRoster actually
        /// depends on now. Works fine as long as portal data coverage matches
        /// roster/recruiting coverage, but isn't guaranteed to.
        /// </summary>
        public async Task<int> ComputeZRosterBulkAsync(CancellationToken token = default)
        {
            var seasons = await _uow.Portal.GetDistinctSeasonsAsync(token);
            int total = 0;
            foreach (var season in seasons)
                total += await ComputeZRosterAsync(season, token);
            return total;
        }

        private static double WeightedDefensiveValue(string statType, double statValue) => statType switch
        {
            "TOT"   => statValue * 1.0,
            "TFL"   => statValue * 2.0,
            "SACKS" => statValue * 3.0,
            _       => 0.0
        };
    }
}
