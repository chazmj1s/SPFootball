using SaturdayPulse.Contracts;
using SaturdayPulse.Contracts.Responses;
using SaturdayPulse.ModelViews;
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
        /// Computes and persists ZRoster, RecruitingComposite, PortalInComposite, and
        /// PortalOutComposite for all FBS teams for the given season, in one pass —
        /// deliberately one trigger, not several, so there's only one thing to
        /// remember to run and the four values can't drift out of sync with each
        /// other. Returns count of teams that got at least one field updated. Does
        /// NOT write OffensiveZScore/DefensiveZScore — those belong to
        /// DeveloperService's scoring-based season rollup.
        ///
        /// Unlike the ZRoster-only version this replaces, a season with no roster-
        /// talent data (talent == null) no longer short-circuits the whole method —
        /// Recruiting/Portal composites have an independent data source (RecruitPlayer
        /// / PortalEntry, not RosterPlayer) and should still compute even if roster
        /// data specifically isn't loaded for the year.
        /// </summary>
        public async Task<int> ComputeZRosterAsync(int season, CancellationToken token = default)
        {
            var talent = await ComputeTalentTotalsAsync(season, token);
            var totalZByTeamId = talent != null
                ? ComputeZScores(talent.TotalTalentByTeamId)
                : new Dictionary<int, double>();

            var teamsById = await _uow.Teams.GetDictionaryByTeamIdAsync(token);
            var recruitingByTeamName = await ComputeRecruitingCompositesAsync(season, token);
            var portalInByTeamName = await ComputePortalCompositesAsync(season, incoming: true, token);
            var portalOutByTeamName = await ComputePortalCompositesAsync(season, incoming: false, token);

            var teamRecords = await _uow.TeamRecords.GetByYearAsync(season, token);

            int updated = 0;
            foreach (var record in teamRecords)
            {
                var changed = false;

                if (totalZByTeamId.TryGetValue(record.TeamID, out var z))
                {
                    record.ZRoster = (decimal)z;
                    changed = true;
                }

                if (teamsById.TryGetValue(record.TeamID, out var team) && !string.IsNullOrEmpty(team.TeamName))
                {
                    if (recruitingByTeamName.TryGetValue(team.TeamName, out var rc) && rc.HasValue)
                    {
                        record.RecruitingComposite = (decimal)rc.Value;
                        changed = true;
                    }
                    if (portalInByTeamName.TryGetValue(team.TeamName, out var pin) && pin.HasValue)
                    {
                        record.PortalInComposite = (decimal)pin.Value;
                        changed = true;
                    }
                    if (portalOutByTeamName.TryGetValue(team.TeamName, out var pout) && pout.HasValue)
                    {
                        record.PortalOutComposite = (decimal)pout.Value;
                        changed = true;
                    }
                }

                if (changed) updated++;
            }

            await _uow.SaveChangesAsync(token);

            return updated;
        }

        /// <summary>
        /// Weighted-mean Recruiting composite (×10, see WeightedMeanRatingDisplay) for
        /// every team with at least one committed recruit in `season`, keyed by team
        /// name (RecruitPlayer.CommittedTo is a name, not a TeamID — same join
        /// constraint GetRosterChangesAsync already works under).
        /// </summary>
        private async Task<Dictionary<string, double?>> ComputeRecruitingCompositesAsync(
            int season, CancellationToken token)
        {
            var recruits = await _uow.RecruitPlayers.GetByYearAsync(season, token);
            return recruits
                .Where(r => !string.IsNullOrEmpty(r.CommittedTo))
                .GroupBy(r => r.CommittedTo!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => WeightedMeanRatingDisplay(g.Select(r => (r.Rating, WeightFor(r.Position)))),
                    StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Weighted-mean Portal In/Out composite (×10) per team for `season`, keyed by
        /// team name. Out values are pre-negated here — see PortalOutComposite's own
        /// doc on TeamRecord. Same "ungraded" Rating==0/null exclusion as
        /// GetRosterChangesAsync's per-request version used before this cache existed.
        /// </summary>
        private async Task<Dictionary<string, double?>> ComputePortalCompositesAsync(
            int season, bool incoming, CancellationToken token)
        {
            var portalForSeason = await _uow.Portal.GetBySeasonAsync(season, token);

            var relevant = incoming
                ? portalForSeason.Where(p =>
                    !string.IsNullOrEmpty(p.Destination)
                    && !string.Equals(p.Eligibility, "Withdrawn", StringComparison.OrdinalIgnoreCase))
                : portalForSeason.Where(p =>
                    !string.IsNullOrEmpty(p.Origin)
                    && !string.Equals(p.Eligibility, "Withdrawn", StringComparison.OrdinalIgnoreCase)
                    && p.Destination != null);

            var groupKey = incoming
                ? (Func<PortalEntry, string>)(p => p.Destination!)
                : (p => p.Origin!);

            var result = relevant
                .GroupBy(groupKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g =>
                    {
                        var ratedOnly = g
                            .Where(p => p.Rating.HasValue && p.Rating.Value > 0)
                            .Select(p => (p.Rating!.Value, WeightFor(p.Position)));
                        var mean = WeightedMeanRatingDisplay(ratedOnly);
                        return incoming ? mean : NegateIfPresent(mean);
                    },
                    StringComparer.OrdinalIgnoreCase);

            return result;
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
        /// Assembles the Roster Changes popup data for one team: current+prior ZRoster
        /// with national ordinal rank, this year's signing class, portal in/out, and a
        /// plain RosterPlayer diff (this team's `year` roster vs its `year - 1` roster,
        /// both already loaded separately per RosterPlayer.cs's own remarks).
        ///
        /// Read-only — nothing persisted. Throws KeyNotFoundException if teamId doesn't
        /// resolve to a known team, matching GetPowerRankingsV2Async's convention so the
        /// controller's existing catch (KeyNotFoundException) -> NotFound handling applies
        /// unchanged.
        ///
        /// Known caveats (see the Roster Changes Popup proposal for full detail):
        /// - ZRoster/rank will be null on either side if ComputeZRosterAsync hasn't been
        ///   run for that year — this is a real "no data" state, not a bug, and callers
        ///   should render it as such rather than as a zero/worst rank.
        /// - RosterPlayer's own remarks flag that one PlayerId can appear under two Team
        ///   values within a single season (mid-season transfer). GetByTeamAndSeasonAsync
        ///   is scoped to (team, season) so this method only ever sees this team's own
        ///   snapshot rows — a player mid-transfer could show up as "new" here and also as
        ///   "departed" on their prior team's card in the same season. Not resolved here.
        /// </summary>
        public async Task<RosterChangesResult> GetRosterChangesAsync(
            int teamId, int year, CancellationToken token = default)
        {
            var teamsById = await _uow.Teams.GetDictionaryByTeamIdAsync(token);
            if (!teamsById.TryGetValue(teamId, out var team) || string.IsNullOrEmpty(team.TeamName))
                throw new KeyNotFoundException($"No team found for teamId {teamId}.");

            var teamName = team.TeamName;
            var priorYear = year - 1;

            // ── Roster strength (current + prior ZRoster, national ordinal rank,
            // and a 0-10 display rating via the standard normal CDF — see Phi()) ──
            var teamRecordsForYear = await _uow.TeamRecords.GetByYearAsync(year, token);
            var teamRecordsForPriorYear = await _uow.TeamRecords.GetByYearAsync(priorYear, token);

            var rankByTeam = teamRecordsForYear
                .Where(r => r.ZRoster.HasValue)
                .OrderByDescending(r => r.ZRoster!.Value)
                .Select((r, i) => new { r.TeamID, Rank = i + 1 })
                .ToDictionary(x => x.TeamID, x => x.Rank);

            var priorRankByTeam = teamRecordsForPriorYear
                .Where(r => r.ZRoster.HasValue)
                .OrderByDescending(r => r.ZRoster!.Value)
                .Select((r, i) => new { r.TeamID, Rank = i + 1 })
                .ToDictionary(x => x.TeamID, x => x.Rank);

            var currentRecord = teamRecordsForYear.FirstOrDefault(r => r.TeamID == teamId);
            var priorRecord = teamRecordsForPriorYear.FirstOrDefault(r => r.TeamID == teamId);

            var currentZRoster = (double?)currentRecord?.ZRoster;
            var priorZRoster = (double?)priorRecord?.ZRoster;

            var rosterStrength = new RosterStrengthDto
            {
                CurrentZRoster = currentZRoster,
                CurrentRank = rankByTeam.TryGetValue(teamId, out var cr) ? cr : (int?)null,
                PriorZRoster = priorZRoster,
                PriorRank = priorRankByTeam.TryGetValue(teamId, out var pr) ? pr : (int?)null,
                CurrentRatingDisplay = currentZRoster.HasValue ? 10.0 * Phi(currentZRoster.Value) : (double?)null,
                PriorRatingDisplay = priorZRoster.HasValue ? 10.0 * Phi(priorZRoster.Value) : (double?)null
            };

            // ── Recruiting/Portal composites — read from TeamRecords, not
            // recomputed. Populated by ComputeZRosterAsync (same trigger as
            // ZRoster) — same coverage caveat: null until that op has been run
            // for the year, not a zero composite. ──
            var recruitingComposite = new RosterChangeMetricDto
            {
                Current = (double?)currentRecord?.RecruitingComposite,
                Prior = (double?)priorRecord?.RecruitingComposite
            };

            var portalInComposite = new RosterChangeMetricDto
            {
                Current = (double?)currentRecord?.PortalInComposite,
                Prior = (double?)priorRecord?.PortalInComposite
            };

            var portalOutComposite = new RosterChangeMetricDto
            {
                Current = (double?)currentRecord?.PortalOutComposite,
                Prior = (double?)priorRecord?.PortalOutComposite
            };

            var portalNetComposite = new RosterChangeMetricDto
            {
                Current = SumIfEitherPresent(portalInComposite.Current, portalOutComposite.Current),
                Prior = SumIfEitherPresent(portalInComposite.Prior, portalOutComposite.Prior)
            };

            // ── Recruiting class list — current year only, per spec. Still a live
            // per-request fetch: individual player detail isn't cached, only the
            // rollup composite above is. ──
            var recruitsForYear = await _uow.RecruitPlayers.GetByYearAsync(year, token);

            var recruitingClass = recruitsForYear
                .Where(r => string.Equals(r.CommittedTo, teamName, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(r => r.Rating)
                .Select(r => new RecruitSummaryDto
                {
                    Name = r.Name,
                    Position = r.Position,
                    Stars = r.Stars,
                    Rating = r.Rating,
                    Ranking = r.Ranking
                })
                .ToList();

            // ── Portal in/out lists — current year only, per spec, same reasoning
            // as recruitingClass above. Exact filter per PortalEntry.cs's own class
            // doc. ──
            var portalForYear = await _uow.Portal.GetBySeasonAsync(year, token);

            var portalIn = portalForYear
                .Where(p => string.Equals(p.Destination, teamName, StringComparison.OrdinalIgnoreCase)
                         && !string.Equals(p.Eligibility, "Withdrawn", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(p => p.Rating ?? 0)
                .Select(p => new PortalTransferDto
                {
                    Name = $"{p.FirstName} {p.LastName}".Trim(),
                    Position = p.Position,
                    Rating = p.Rating,
                    OtherTeam = p.Origin
                })
                .ToList();

            var portalOut = portalForYear
                .Where(p => string.Equals(p.Origin, teamName, StringComparison.OrdinalIgnoreCase)
                         && !string.Equals(p.Eligibility, "Withdrawn", StringComparison.OrdinalIgnoreCase)
                         && p.Destination != null)
                .OrderByDescending(p => p.Rating ?? 0)
                .Select(p => new PortalTransferDto
                {
                    Name = $"{p.FirstName} {p.LastName}".Trim(),
                    Position = p.Position,
                    Rating = p.Rating,
                    OtherTeam = p.Destination
                })
                .ToList();

            // ── Retained / Departed / New — plain RosterPlayer diff, catches every
            // departure regardless of reason (draft, graduation, attrition), not just
            // portal-out. See the mid-season-transfer caveat in this method's doc.
            // Kept in the response for the future Roster page — not rendered in the
            // current summary panel, per 2026-08 UX decision. ──
            var currentRoster = await _uow.RosterPlayers.GetByTeamAndSeasonAsync(teamName, year, token);
            var priorRoster = await _uow.RosterPlayers.GetByTeamAndSeasonAsync(teamName, priorYear, token);

            var currentIds = currentRoster.Select(r => r.PlayerId).ToHashSet();
            var priorIds = priorRoster.Select(r => r.PlayerId).ToHashSet();

            static PlayerSummaryDto ToPlayerSummary(RosterPlayer r) => new()
            {
                Name = $"{r.FirstName} {r.LastName}".Trim(),
                Position = r.Position,
                ClassYear = r.ClassYear
            };

            var retained = currentRoster
                .Where(r => priorIds.Contains(r.PlayerId))
                .Select(ToPlayerSummary)
                .ToList();

            var newPlayers = currentRoster
                .Where(r => !priorIds.Contains(r.PlayerId))
                .Select(ToPlayerSummary)
                .ToList();

            var departed = priorRoster
                .Where(r => !currentIds.Contains(r.PlayerId))
                .Select(ToPlayerSummary)
                .ToList();

            return new RosterChangesResult(
                teamId,
                teamName,
                year,
                rosterStrength,
                recruitingComposite,
                portalInComposite,
                portalOutComposite,
                portalNetComposite,
                recruitingClass,
                portalIn,
                portalOut,
                retained,
                departed,
                newPlayers);
        }

        /// <summary>Position weight lookup shared by ZRoster and the Roster Changes
        /// composites — same fallback (1.0 for unmapped positions, e.g. "ATH" in
        /// recruiting data) as EffectiveRating uses.</summary>
        private static double WeightFor(string? position) =>
            PositionWeights.GetValueOrDefault((position ?? string.Empty).ToUpper().Trim(), 1.0);

        /// <summary>
        /// Weighted MEAN (not weighted sum) of Rating across the given players,
        /// scaled ×10 for display. Rating is natively 0.0-1.0, and a weighted mean of
        /// bounded values is itself bounded by the same range regardless of the
        /// weights used — so this ×10 is an exact linear rescale, not an arbitrary
        /// display multiplier. Returns null if the player set is empty (no usable
        /// signal — "no data," not a zero composite).
        /// </summary>
        private static double? WeightedMeanRatingDisplay(IEnumerable<(double Rating, double Weight)> players)
        {
            var list = players.ToList();
            if (list.Count == 0) return null;

            var weightSum = list.Sum(p => p.Weight);
            if (weightSum <= 0) return null;

            var weightedSum = list.Sum(p => p.Rating * p.Weight);
            return 10.0 * (weightedSum / weightSum);
        }

        private static double? NegateIfPresent(double? value) => value.HasValue ? -value.Value : null;

        private static double? SumIfEitherPresent(double? a, double? b) =>
            (a.HasValue || b.HasValue) ? (a ?? 0) + (b ?? 0) : null;

        /// <summary>
        /// Standard normal CDF, Abramowitz &amp; Stegun 7.1.26 approximation
        /// (max absolute error ~7.5e-8) — .NET has no built-in Φ. Used to convert
        /// ZRoster (an unbounded Z-score, unlike the composite metrics above) onto a
        /// comparable 0-10 display scale. This assumes roster-talent Z-scores are
        /// approximately normally distributed across the FBS population — a real
        /// statistical assumption, flagged as such, not a certainty.
        /// </summary>
        private static double Phi(double z)
        {
            var sign = z < 0 ? -1.0 : 1.0;
            var x = Math.Abs(z) / Math.Sqrt(2.0);

            const double a1 = 0.254829592;
            const double a2 = -0.284496736;
            const double a3 = 1.421413741;
            const double a4 = -1.453152027;
            const double a5 = 1.061405429;
            const double p = 0.3275911;

            var t = 1.0 / (1.0 + p * x);
            var y = 1.0 - (((((a5 * t + a4) * t) + a3) * t + a2) * t + a1) * t * Math.Exp(-x * x);

            return 0.5 * (1.0 + sign * y);
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
