using SaturdayPulse.Contracts;
using SaturdayPulse.Models;

namespace SaturdayPulse.Services
{
    /// <summary>
    /// Year-aware conference classification service.
    ///
    /// Replaces the static string-match in RatingCalculator.GetConferenceTier with
    /// TeamsConferenceHistory + Conferences lookups (via IUnitOfWork) so that:
    ///
    ///   1. Conference filters on the mobile UI show historically-correct team lists
    ///      (Nebraska appears under Big 12 in 1998, Big Ten in 2012,
    ///       Texas appears under SWC through 1995, Big 12 from 1996).
    ///
    ///   2. Tier weights in the ratings pipeline reflect actual competitive level
    ///      for the year being processed — no G5 over-performance leak from
    ///      current-era string matching.
    ///
    /// Repository boundaries
    /// ─────────────────────
    /// TeamsConferenceHistoryRepository → teamId → conferenceId (year-filtered)
    /// ConferenceRepository             → conferenceId → Conference row
    /// ConferenceTierService            → joins in memory, applies tier logic
    ///
    /// Tier classification rules
    /// ─────────────────────────
    /// P4    — ConferenceId in the P4 set below (current + historical predecessors)
    /// G5    — Classification = "fbs" and not P4
    /// FCS   — Classification = "fcs"
    /// Other — DII, DIII, or no history row found for that year
    ///
    /// Historical P4 predecessor chain
    /// ────────────────────────────────
    ///   Big 6  (207) → Big 7 (210) → Big 8 (214) → Big 12 (4)
    ///   AAWU   (213) → Pac-8 (216) → Pac-10 (220) → Pac-12 (9)
    ///   SWC    (204) — dissolved 1995; members joined Big 12
    ///   Big East (222) — P4 while fbs-classified (1991–2012);
    ///                    AAC (151) took over football membership in 2013 → G5
    /// </summary>
    public class ConferenceTierService
    {
        private readonly IUnitOfWork _uow;

        public ConferenceTierService(IUnitOfWork uow)
        {
            _uow = uow;
        }

        // ── P4 conference IDs ─────────────────────────────────────────────────────

        private static readonly HashSet<int> P4ConferenceIds = new()
        {
            1,   // ACC               current
            4,   // Big 12            current
            5,   // Big Ten           current
            8,   // SEC               current
            9,   // Pac-12            2011–2023 (dissolved; members moved to Big 12/Ten)
            204, // SWC               1915–1995
            207, // Big 6             1928–1947
            210, // Big 7             1948–1959
            214, // Big 8             1960–1995
            213, // AAWU              1959–1967
            216, // Pac-8             1968–1977
            220, // Pac-10            1978–2010
        };

        // Big East: P4 only through 2012; AAC took over football membership in 2013
        private const int BigEastConferenceId = 222;
        private const int BigEastLastP4Year   = 2012;

        // ── Conference info tuple ─────────────────────────────────────────────────

        /// <summary>
        /// Year-appropriate conference identity for a team.
        /// Name and Abbreviation are for display; Tier drives filter and ratings logic.
        /// </summary>
        public record ConferenceData(string Name, string Abbreviation, string Tier);

        // ── Primary API ───────────────────────────────────────────────────────────

        /// <summary>
        /// Returns year-appropriate ConferenceData for a single team.
        /// Prefer the batch overload when processing multiple teams.
        /// </summary>
        public async Task<ConferenceData> GetConfDataAsync(
            int teamId, int year, CancellationToken token = default)
        {
            var result = await GetConfDataBatchAsync(new[] { teamId }, year, token);
            return result.TryGetValue(teamId, out var cd)
                ? cd
                : new ConferenceData(string.Empty, string.Empty, "Other");
        }

        /// <summary>
        /// Batch version — resolves Name, Abbreviation, and Tier for a list of team IDs
        /// in two repository calls (one per table), joined in memory here.
        ///
        /// Used by GetScheduleV2Async, GetPostseasonGamesV2Async, GetPowerRankingsV2Async,
        /// and WeeklyRankingsService to avoid N+1 queries.
        ///
        /// Teams with no history row for the year are absent from the result;
        /// callers should fall back to GetTierStatic() for missing keys.
        /// </summary>
        public async Task<Dictionary<int, ConferenceData>> GetConfDataBatchAsync(
            IEnumerable<int> teamIds, int year, CancellationToken token = default)
        {
            var confIdByTeamId = await _uow.TeamsConferenceHistory
                .GetConferenceIdsByYearAsync(year, token);

            var confById = await _uow.Conferences
                .GetDictionaryAsync(token);

            var ids = teamIds.ToHashSet();

            return confIdByTeamId
                .Where(kvp => ids.Contains(kvp.Key) && confById.ContainsKey(kvp.Value))
                .ToDictionary(
                    kvp => kvp.Key,
                    kvp =>
                    {
                        var c    = confById[kvp.Value];
                        var tier = GetTierFromConference(c.ConferenceId, c.Classification, year);
                        return new ConferenceData(
                            c.Name         ?? string.Empty,
                            c.Abbreviation ?? string.Empty,
                            tier);
                    });
        }

        // ── Convenience: abbreviation+tier only (for schedule stamping) ───────────

        /// <summary>
        /// Batch shorthand returning only (Abbreviation, Tier) — used by
        /// GetScheduleV2Async / GetPostseasonGamesV2Async where Name is not needed.
        /// Delegates to GetConfDataBatchAsync; no extra DB calls.
        /// </summary>
        public async Task<Dictionary<int, (string Abbreviation, string Tier)>> GetConfAndTierBatchAsync(
            IEnumerable<int> teamIds, int year, CancellationToken token = default)
        {
            var full = await GetConfDataBatchAsync(teamIds, year, token);
            return full.ToDictionary(kvp => kvp.Key, kvp => (kvp.Value.Abbreviation, kvp.Value.Tier));
        }

        // ── Tier classification ───────────────────────────────────────────────────

        /// <summary>
        /// Public wrapper so ProductionGameDataService can classify a conference
        /// directly when building the year-aware conference list for the dropdown.
        /// All other callers should use the async batch methods above.
        /// </summary>
        public string ClassifyConference(int conferenceId, string? classification, int year)
            => GetTierFromConference(conferenceId, classification, year);

        private static string GetTierFromConference(int conferenceId, string? classification, int year)
        {
            if (!string.Equals(classification, "fbs", StringComparison.OrdinalIgnoreCase))
            {
                return string.Equals(classification, "fcs", StringComparison.OrdinalIgnoreCase)
                    ? "FCS"
                    : "Other";
            }

            // Big East was P4-equivalent while it had FBS football members
            if (conferenceId == BigEastConferenceId)
                return year <= BigEastLastP4Year ? "P4" : "G5";

            return P4ConferenceIds.Contains(conferenceId) ? "P4" : "G5";
        }

        // ── Static fallback ───────────────────────────────────────────────────────

        /// <summary>
        /// Year-blind fallback for teams absent from TeamsConferenceHistory
        /// (e.g. Notre Dame independent, FCS opponents with no history row).
        /// Prefer the async methods for all production code.
        /// </summary>
        public static string GetTierStatic(string? conferenceName, string? teamName = null)
            => teamName switch
            {
                "Notre Dame"  => "P4",
                "Connecticut" => "G5",
                _ => conferenceName switch
                {
                    "SEC"               => "P4",
                    "Big Ten"           => "P4",
                    "Big 12"            => "P4",
                    "ACC"               => "P4",
                    "American Athletic" => "G5",
                    "Mountain West"     => "G5",
                    "Sun Belt"          => "G5",
                    "Mid-American"      => "G5",
                    "Conference USA"    => "G5",
                    _                   => "Other"
                }
            };
    }
}
