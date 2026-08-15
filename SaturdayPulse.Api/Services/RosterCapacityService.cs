using SaturdayPulse.Contracts;
using SaturdayPulse.Models;

namespace SaturdayPulse.Services
{
    /// <summary>
    /// Computes an absolute Week-0 roster composite talent valuation. Two outputs:
    ///
    /// 1. ZRoster — national Z-score of each FBS team's top-45 talent core (Offensive
    ///    + Defensive + Specialist talent points combined), persisted directly to
    ///    TeamRecord.ZRoster by ComputeZRosterAsync.
    ///
    /// 2. Year-over-year Offensive/Defensive talent Z-score DELTAS, returned (not
    ///    persisted here) by GetRosterZScoreDeltasAsync, for DeveloperService to fold
    ///    into its own weighted-history AvgPointsScored/AvgPointsAllowed baseline
    ///    before that baseline is Z-scored into TeamRecord.OffensiveZScore/
    ///    DefensiveZScore. TeamRecord.OffensiveZScore/DefensiveZScore are a season
    ///    rollup of WeeklyRanking's scoring-based metric and are NOT written by this
    ///    class — only ZRoster is.
    ///
    /// Replaces the prior delta-transaction model (inflow talent minus departed
    /// production, national-Z'd, minus a coaching-turnover penalty). This version
    /// extracts each FBS team's top-45 talent core directly from a single season's
    /// roster, classifies each player into Offensive/Defensive/Specialist, weights by
    /// positional importance, and Z-scores the resulting Offensive, Defensive, and
    /// Total Core talent sums against the full FBS population for that season.
    ///
    /// No PlayerStats or CoachRecords dependency remains — reads only Teams,
    /// RosterPlayers, and TeamRecords.
    /// </summary>
    public class RosterCapacityService
    {
        private readonly IUnitOfWork _uow;

        // Position tier weights — exact mapping per spec. Comparer is
        // OrdinalIgnoreCase so lookups tolerate raw casing, but positions are
        // still normalized via .ToUpper().Trim() before any lookup or set
        // membership check, per the no-loose-string-parsing constraint.
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
            ["DB"]   = 1.5,
            ["WR"]   = 1.5,
            ["LB"]   = 1.5,
            ["RB"]   = 1.5,
            ["S"]    = 1.0,
            ["TE"]   = 1.0,
            ["K"]    = 1.0,
            ["PK"]   = 1.0,
            ["P"]    = 1.0,
            ["LS"]   = 1.0,
        };

        private static readonly HashSet<string> OffensivePositions =
            new(StringComparer.OrdinalIgnoreCase) { "QB", "OT", "IOL", "OL", "WR", "RB", "TE" };

        private static readonly HashSet<string> DefensivePositions =
            new(StringComparer.OrdinalIgnoreCase) { "DE", "DL", "EDGE", "CB", "DB", "LB", "S" };

        private static readonly HashSet<string> SpecialistPositions =
            new(StringComparer.OrdinalIgnoreCase) { "K", "PK", "P", "LS" };

        // FBS baseline replacement score for a standard position player with no
        // usable TransferRating or RecruitRating.
        private const double StandardBaselineRating = 0.80;

        // Baseline for specialists (K/PK/P/LS) with no usable rating — kept at 0.0
        // so specialist volume can't bloat offensive/defensive team strength.
        private const double SpecialistBaselineRating = 0.0;

        // Two-deep filter: only the top N players by individual talent points
        // (EffectiveRating * PositionWeight) count toward a team's core.
        private const int TopRosterSlice = 45;

        public RosterCapacityService(IUnitOfWork uow) => _uow = uow;

        /// <summary>
        /// Computes and persists ZRoster for all FBS teams for the given season, using
        /// that season's roster only. Returns count of teams updated. Does NOT write
        /// OffensiveZScore/DefensiveZScore — those belong to DeveloperService's
        /// scoring-based season rollup.
        /// </summary>
        public async Task<int> ComputeZRosterAsync(int season, CancellationToken token = default)
        {
            var talent = await ComputeTalentTotalsAsync(season, token);
            if (talent == null) return 0;

            var totalZByTeamId = ComputeZScores(talent.TotalTalentByTeamId);

            var teamRecords = await _uow.TeamRecords.GetByYearAsync(season, token);

            int updated = 0;
            foreach (var record in teamRecords)
            {
                if (!totalZByTeamId.TryGetValue(record.TeamID, out var z)) continue;

                record.ZRoster = (decimal)z;
                updated++;
            }

            await _uow.SaveChangesAsync(token);

            return updated;
        }

        /// <summary>
        /// Runs ComputeZRosterAsync for every season that has portal data loaded.
        /// NOTE: still driven off IPortalRepository.GetDistinctSeasonsAsync (i.e.
        /// PortalEntries) rather than RosterPlayers — a known, pre-existing, flagged
        /// mismatch. Unchanged here; out of scope for the absolute-valuation switch.
        /// </summary>
        public async Task<int> ComputeZRosterBulkAsync(CancellationToken token = default)
        {
            var seasons = await _uow.Portal.GetDistinctSeasonsAsync(token);
            int total = 0;
            foreach (var season in seasons)
                total += await ComputeZRosterAsync(season, token);
            return total;
        }

        /// <summary>
        /// Computes each FBS team's Offensive/Defensive talent Z-score for `season`
        /// and `season - 1` independently (each Z-scored against its own season's FBS
        /// population — NOT a shared population across both years), and returns the
        /// year-over-year delta (season minus season-1) per team, keyed by TeamId.
        ///
        /// A team is omitted from the result if it has no usable talent total for
        /// EITHER season (e.g. new to FBS, or roster data not yet loaded for
        /// season - 1) — callers should treat a missing entry as "no roster signal,
        /// leave the baseline untouched" rather than assuming a zero delta.
        /// </summary>
        public async Task<Dictionary<int, (double OffensiveDelta, double DefensiveDelta)>> GetRosterZScoreDeltasAsync(
            int season, CancellationToken token = default)
        {
            var current = await ComputeTalentTotalsAsync(season, token);
            var prior = await ComputeTalentTotalsAsync(season - 1, token);

            var result = new Dictionary<int, (double OffensiveDelta, double DefensiveDelta)>();
            if (current == null || prior == null) return result;

            var currentOffZ = ComputeZScores(current.OffensiveTalentByTeamId);
            var currentDefZ = ComputeZScores(current.DefensiveTalentByTeamId);
            var priorOffZ = ComputeZScores(prior.OffensiveTalentByTeamId);
            var priorDefZ = ComputeZScores(prior.DefensiveTalentByTeamId);

            foreach (var teamId in currentOffZ.Keys)
            {
                if (!priorOffZ.TryGetValue(teamId, out var priorOff)) continue;
                if (!priorDefZ.TryGetValue(teamId, out var priorDef)) continue;
                if (!currentDefZ.TryGetValue(teamId, out var currentDef)) continue;

                var currentOff = currentOffZ[teamId];
                result[teamId] = (currentOff - priorOff, currentDef - priorDef);
            }

            return result;
        }

        /// <summary>
        /// Per-team top-45 talent totals (Offensive/Defensive/Total) for a single
        /// season, keyed by TeamId. Returns null if the season has no FBS roster data
        /// at all (rather than an empty-but-non-null result), so callers can
        /// distinguish "no data for this season" from "data loaded, zero talent".
        /// </summary>
        private async Task<TalentTotals?> ComputeTalentTotalsAsync(int season, CancellationToken token)
        {
            var allTeams = await _uow.Teams.GetAllAsync(token);
            var fbsTeams = allTeams
                .Where(t => string.Equals(t.Division, "fbs", StringComparison.OrdinalIgnoreCase))
                .ToList();
            var fbsNameToId = fbsTeams
                .ToDictionary(t => t.TeamName, t => t.TeamId, StringComparer.OrdinalIgnoreCase);

            // GetBySeasonAsync returns all teams for the season — no FBS filter
            // available at the repo level, so it's applied client-side afterward,
            // matching the existing two-step convention elsewhere in this pipeline.
            var rosterAll = await _uow.RosterPlayers.GetBySeasonAsync(season, token);
            var roster = rosterAll
                .Where(r => fbsNameToId.ContainsKey(r.Team))
                .ToList();

            if (roster.Count == 0) return null;

            var rosterByTeam = roster
                .GroupBy(r => r.Team, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            var offensiveTalentByTeamId = new Dictionary<int, double>();
            var defensiveTalentByTeamId = new Dictionary<int, double>();
            var totalTalentByTeamId = new Dictionary<int, double>();

            foreach (var (team, players) in rosterByTeam)
            {
                if (!fbsNameToId.TryGetValue(team, out var teamId)) continue;

                var scoredPlayers = players
                    .Select(p =>
                    {
                        var normalizedPosition = (p.Position ?? string.Empty).ToUpper().Trim();
                        var weight = PositionWeights.GetValueOrDefault(normalizedPosition, 1.0);
                        var effectiveRating = EffectiveRating(p, normalizedPosition);
                        return new
                        {
                            Position = normalizedPosition,
                            TalentPoints = effectiveRating * weight
                        };
                    })
                    .OrderByDescending(x => x.TalentPoints)
                    .Take(TopRosterSlice)
                    .ToList();

                var offensiveTalent = scoredPlayers
                    .Where(x => OffensivePositions.Contains(x.Position))
                    .Sum(x => x.TalentPoints);

                var defensiveTalent = scoredPlayers
                    .Where(x => DefensivePositions.Contains(x.Position))
                    .Sum(x => x.TalentPoints);

                var totalTalent = scoredPlayers.Sum(x => x.TalentPoints);

                offensiveTalentByTeamId[teamId] = offensiveTalent;
                defensiveTalentByTeamId[teamId] = defensiveTalent;
                totalTalentByTeamId[teamId] = totalTalent;
            }

            if (totalTalentByTeamId.Count == 0) return null;

            return new TalentTotals(offensiveTalentByTeamId, defensiveTalentByTeamId, totalTalentByTeamId);
        }

        /// <summary>
        /// Selects TransferRating if present and within [0.0, 1.0]; otherwise
        /// RecruitRating if present and within [0.0, 1.0]; otherwise the FBS
        /// baseline replacement score (0.0 for specialists, 0.80 for everyone else).
        /// </summary>
        private static double EffectiveRating(RosterPlayer player, string normalizedPosition)
        {
            if (player.TransferRating.HasValue &&
                player.TransferRating.Value >= 0.0 && player.TransferRating.Value <= 1.0)
            {
                return player.TransferRating.Value;
            }

            if (player.RecruitRating.HasValue &&
                player.RecruitRating.Value >= 0.0 && player.RecruitRating.Value <= 1.0)
            {
                return player.RecruitRating.Value;
            }

            return SpecialistPositions.Contains(normalizedPosition)
                ? SpecialistBaselineRating
                : StandardBaselineRating;
        }

        private static Dictionary<int, double> ComputeZScores(Dictionary<int, double> rawByTeamId)
        {
            var mean = rawByTeamId.Values.Average();
            var stdDev = rawByTeamId.Count > 1
                ? Math.Sqrt(rawByTeamId.Values.Average(v => Math.Pow(v - mean, 2)))
                : 0.0;

            return rawByTeamId.ToDictionary(
                kvp => kvp.Key,
                kvp => stdDev > 0 ? Math.Round((kvp.Value - mean) / stdDev, 4) : 0.0);
        }

        private sealed record TalentTotals(
            Dictionary<int, double> OffensiveTalentByTeamId,
            Dictionary<int, double> DefensiveTalentByTeamId,
            Dictionary<int, double> TotalTalentByTeamId);
    }
}
