using SaturdayPulse.Contracts.Requests;
using SaturdayPulse.Contracts.Responses;
using SaturdayPulse.Interfaces;
using SaturdayPulse.Models;

namespace SaturdayPulse.Services
{
    /// <summary>
    /// Projects the 12-team CFP field off current WeeklyRankings data.
    ///
    /// Auto bids (per Charlie, 2026-09-14, P4 rule updated 2026-09-15):
    ///   - The actual or projected CHAMPION of each of ACC, B1G, B12, SEC —
    ///     real winner if that conference's title game has been played,
    ///     otherwise the matchup engine's projected winner (see
    ///     ProductionGameDataService.GetConferenceChampionsAsync). No manual
    ///     rating adjustment for an unplayed/projected result — the champion's
    ///     existing Ranking is used as-is; once a real title game is actually
    ///     played, the normal weekly rating pipeline picks it up like any
    ///     other game, same as always.
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

            // ── P4 auto bids: actual/projected conference champion ─────────
            var conferenceChampions = await _gameDataService.GetConferenceChampionsAsync(year, week, token);

            foreach (var conf in P4Conferences)
            {
                if (!conferenceChampions.TryGetValue(conf, out var championTeamId))
                {
                    result.Log.Add($"No resolvable champion for auto-bid conference {conf} — skipped.");
                    continue;
                }

                var champ = pool.FirstOrDefault(r => r.TeamID == championTeamId);
                if (champ == null)
                {
                    result.Log.Add($"{conf} champion (TeamID {championTeamId}) not found in current rankings pool — skipped.");
                    continue;
                }

                selected.Add(champ);
                reasons[champ.TeamID] = conf;
                result.Log.Add($"{conf} auto bid: {champ.TeamName} (conference champion, Ranking {champ.Ranking:0.0000}).");
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

        // ─────────────────────────────────────────────────────────────────
        // Projected bracket — First Round through National Championship,
        // computed in order via the matchup engine. No toggle, no partial
        // state (Charlie, 2026-09-15).
        // ─────────────────────────────────────────────────────────────────

        /// <summary>Carries a team's bracket seed alongside its name through
        /// each round — a team's seed doesn't change as it advances, but
        /// which team occupies a given bracket slot does once upsets happen.</summary>
        private class BracketTeam
        {
            public int    Seed     { get; set; }
            public string TeamName { get; set; } = "";
        }

        public async Task<PlayoffBracketResult> GetProjectedBracketAsync(
            int year, int week, CancellationToken token = default)
        {
            var result = new PlayoffBracketResult { Year = year, Week = week };

            var field = await GetProjectedFieldAsync(year, week, token);
            if (field.Field.Count < FieldSize)
            {
                result.Log.Add($"Field has only {field.Field.Count} of {FieldSize} teams — bracket not computed.");
                return result;
            }

            var bySeed = field.Field.ToDictionary(s => s.Seed);
            BracketTeam Bt(int seed) => new() { Seed = seed, TeamName = bySeed[seed].TeamName };

            // ── First Round: 5v12, 6v11, 7v10, 8v9 — higher seed hosts ─────
            var firstRoundPairs = new List<(BracketTeam Home, BracketTeam Away)>
            {
                (Bt(5), Bt(12)),
                (Bt(6), Bt(11)),
                (Bt(7), Bt(10)),
                (Bt(8), Bt(9)),
            };
            var (firstRound, frWinners) = await PredictRoundAsync(
                "First Round", year, week, firstRoundPairs, location: 'H', token);
            result.Rounds.Add(firstRound);

            // ── Quarterfinals: 1v8/9, 2v7/10, 3v6/11, 4v5/12 — neutral site ─
            var qfPairs = new List<(BracketTeam Home, BracketTeam Away)>
            {
                (Bt(1), frWinners[3]), // winner of 8v9
                (Bt(2), frWinners[2]), // winner of 7v10
                (Bt(3), frWinners[1]), // winner of 6v11
                (Bt(4), frWinners[0]), // winner of 5v12
            };
            var (qf, qfWinners) = await PredictRoundAsync(
                "Quarterfinals", year, week, qfPairs, location: 'N', token);
            result.Rounds.Add(qf);

            // ── Semifinals: 1's path vs 4's path, 2's path vs 3's path ─────
            var sfPairs = new List<(BracketTeam Home, BracketTeam Away)>
            {
                (qfWinners[0], qfWinners[3]),
                (qfWinners[1], qfWinners[2]),
            };
            var (sf, sfWinners) = await PredictRoundAsync(
                "Semifinals", year, week, sfPairs, location: 'N', token);
            result.Rounds.Add(sf);

            // ── National Championship ──────────────────────────────────────
            var finalsPairs = new List<(BracketTeam Home, BracketTeam Away)>
            {
                (sfWinners[0], sfWinners[1]),
            };
            var (finals, _) = await PredictRoundAsync(
                "National Championship", year, week, finalsPairs, location: 'N', token);
            result.Rounds.Add(finals);

            return result;
        }

        /// <summary>
        /// Predicts one round's matchups via the batch matchup engine and
        /// resolves each game's winner. Matches predictions back to their
        /// pairs by team name rather than assuming the engine preserves
        /// input order.
        /// </summary>
        private async Task<(BracketRoundResult Round, List<BracketTeam> Winners)> PredictRoundAsync(
            string roundLabel, int year, int week,
            List<(BracketTeam Home, BracketTeam Away)> pairs,
            char location, CancellationToken token)
        {
            var matchups = pairs.Select(p => new MatchupRequest
            {
                TeamName     = p.Home.TeamName,
                OpponentName = p.Away.TeamName,
                Location     = location,
                Week         = week,
            }).ToList();

            var predictions = await _gameDataService.PredictMatchupsAsync(
                year, matchups, token, asOfWeek: week);

            var round   = new BracketRoundResult { RoundLabel = roundLabel };
            var winners = new List<BracketTeam>();

            foreach (var (home, away) in pairs)
            {
                var pred = predictions.FirstOrDefault(p =>
                    p.TeamName == home.TeamName && p.OpponentName == away.TeamName);

                // No prediction found (e.g. a team-name mismatch against the
                // Teams table) — default to the higher seed advancing rather
                // than dropping the matchup from the bracket entirely.
                var homeWins = pred?.IsTeamProjectedWinner ?? true;
                var winner   = homeWins ? home : away;
                winners.Add(winner);

                round.Matchups.Add(new BracketMatchupResult
                {
                    Team1Seed      = home.Seed,
                    Team1Name      = home.TeamName,
                    Team1ProjScore = pred != null ? Math.Round(pred.PredictedTeamScore, 1) : 0,
                    Team2Seed      = away.Seed,
                    Team2Name      = away.TeamName,
                    Team2ProjScore = pred != null ? Math.Round(pred.PredictedOpponentScore, 1) : 0,
                    WinnerSeed     = winner.Seed,
                    WinnerName     = winner.TeamName,
                });
            }

            return (round, winners);
        }
    }
}
