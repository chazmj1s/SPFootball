using SaturdayPulse.Interfaces;
using SaturdayPulse.Models;

namespace SaturdayPulse.Services
{
    /// <summary>
    /// Projects the 12-team CFP field off current WeeklyRankings data only —
    /// no conference championship game simulation (deferred, backlog item 2).
    ///
    /// Auto bids (per Charlie, 2026-09-14):
    ///   - Highest-Ranking team in each of ACC, B1G, B12, SEC (proxy for conference champion)
    ///   - Notre Dame ("Ind"), if inside the top 12 overall by Ranking
    ///   - Single highest-Ranking team across the Group of Six conferences
    ///     (MAC, CUSA, MWC, AAC, PAC, SBC)
    /// Remaining spots up to 12 fill by next-highest Ranking overall (at-large).
    /// Tiebreak order: Ranking desc -> Win% desc -> CombinedSOS desc.
    /// Seeds 1-4 = top four overall Ranking among the 12 selected (bye).
    /// Seeds 5-12 bracket per standard pairing (5v12, 6v11, 7v10, 8v9), no reseeding
    /// (bracket pairing itself is not built here — Field is seed-ordered only;
    /// pairing is a display-layer concern for the mobile side).
    /// </summary>
    public class PlayoffSeedingService : IPlayoffSeedingService
    {
        private static readonly string[] P4Conferences = { "ACC", "B1G", "B12", "SEC" };
        private static readonly string[] G6Conferences  = { "MAC", "CUSA", "MWC", "AAC", "PAC", "SBC" };
        private const string IndependentConference = "Ind";
        private const int    FieldSize = 12;

        private readonly ProductionGameDataService _gameDataService;

        public PlayoffSeedingService(ProductionGameDataService gameDataService)
        {
            _gameDataService = gameDataService;
        }

        public async Task<PlayoffFieldResult> GetProjectedFieldAsync(
            int year, int week, CancellationToken token = default)
        {
            var result = new PlayoffFieldResult { Year = year, Week = week };

            var rankingsResult = await _gameDataService.GetPowerRankingsV2Async(year, week, token);
            if (rankingsResult == null || rankingsResult.Rankings.Count == 0)
            {
                result.Log.Add($"No power rankings data for {year} week {week}.");
                return result;
            }

            var pool = rankingsResult.Rankings.Where(r => r.Ranking.HasValue).ToList();

            var selected = new List<PowerRankingRowResponse>();
            var reasons  = new Dictionary<int, string>(); // TeamID -> AutoBidReason

            // ── P4 auto bids: highest-Ranking team per conference ──────────
            foreach (var conf in P4Conferences)
            {
                var champ = pool
                    .Where(r => string.Equals(r.ConferenceAbbr, conf, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(r => r.Ranking)
                    .ThenByDescending(WinPct)
                    .ThenByDescending(r => r.CombinedSOS)
                    .FirstOrDefault();

                if (champ == null)
                {
                    result.Log.Add($"No teams found for auto-bid conference {conf} — skipped.");
                    continue;
                }

                selected.Add(champ);
                reasons[champ.TeamID] = conf;
                result.Log.Add($"{conf} auto bid: {champ.TeamName} (Ranking {champ.Ranking:0.0000}).");
            }

            // ── Overall order (for ND top-12 check and at-large fill) ──────
            var overallOrder = pool
                .OrderByDescending(r => r.Ranking)
                .ThenByDescending(WinPct)
                .ThenByDescending(r => r.CombinedSOS)
                .ToList();

            // ── Notre Dame auto bid, if inside the top 12 overall ───────────
            var notreDame = pool.FirstOrDefault(r =>
                string.Equals(r.ConferenceAbbr, IndependentConference, StringComparison.OrdinalIgnoreCase));

            if (notreDame != null)
            {
                var overallRank = overallOrder.FindIndex(r => r.TeamID == notreDame.TeamID) + 1;
                if (overallRank is >= 1 and <= FieldSize && !reasons.ContainsKey(notreDame.TeamID))
                {
                    selected.Add(notreDame);
                    reasons[notreDame.TeamID] = "Top 12 finish";
                    result.Log.Add($"Notre Dame auto bid: ranked #{overallRank} overall (Ranking {notreDame.Ranking:0.0000}).");
                }
                else
                {
                    result.Log.Add($"Notre Dame not in top {FieldSize} overall — no auto bid.");
                }
            }

            // ── G6 auto bid: single highest-Ranking team across the pool ───
            var g6Champ = pool
                .Where(r => r.ConferenceAbbr != null &&
                            G6Conferences.Contains(r.ConferenceAbbr, StringComparer.OrdinalIgnoreCase))
                .OrderByDescending(r => r.Ranking)
                .ThenByDescending(WinPct)
                .ThenByDescending(r => r.CombinedSOS)
                .FirstOrDefault();

            if (g6Champ == null)
            {
                result.Log.Add("No Group of Six teams found — skipped G6 auto bid.");
            }
            else if (!reasons.ContainsKey(g6Champ.TeamID))
            {
                selected.Add(g6Champ);
                reasons[g6Champ.TeamID] = "G6";
                result.Log.Add($"G6 auto bid: {g6Champ.TeamName} (Ranking {g6Champ.Ranking:0.0000}).");
            }

            // ── At-large fill: next-highest Ranking overall, until 12 ──────
            foreach (var candidate in overallOrder)
            {
                if (selected.Count >= FieldSize) break;
                if (reasons.ContainsKey(candidate.TeamID)) continue;

                selected.Add(candidate);
                result.Log.Add($"At-large: {candidate.TeamName} (Ranking {candidate.Ranking:0.0000}).");
            }

            if (selected.Count < FieldSize)
                result.Log.Add($"Only {selected.Count} eligible teams found — field is short of {FieldSize}.");

            // ── Final seed order: Ranking desc across the selected 12 ──────
            var seeded = selected
                .OrderByDescending(r => r.Ranking)
                .ThenByDescending(WinPct)
                .ThenByDescending(r => r.CombinedSOS)
                .Select((r, i) => new PlayoffSeed
                {
                    TeamID         = r.TeamID,
                    TeamName       = r.TeamName ?? "",
                    ConferenceAbbr = r.ConferenceAbbr,
                    Seed           = i + 1,
                    Ranking        = r.Ranking,
                    Wins           = r.Wins,
                    Losses         = r.Losses,
                    CombinedSOS    = r.CombinedSOS,
                    AutoBidReason  = reasons.TryGetValue(r.TeamID, out var reason) ? reason : null,
                })
                .ToList();

            result.Field = seeded;
            return result;
        }

        private static double WinPct(PowerRankingRowResponse r)
        {
            var total = r.Wins + r.Losses;
            return total == 0 ? 0d : (double)r.Wins / total;
        }
    }
}
