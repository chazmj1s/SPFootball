using Microsoft.Extensions.Logging;
using SaturdayPulse.Api.Contracts.Responses;
using SaturdayPulse.Contracts;
using SaturdayPulse.Models;
using SaturdayPulse.Data;
using System.Net.Http.Json;
using System.Text.Json;

namespace SaturdayPulse.Utilities
{
    /// <summary>
    /// Calculates and populates matchup-specific historical statistics.
    /// Used to identify high-variance rivalries by comparing actual matchup performance
    /// to expected performance from the live AvgScoreDifferential baseline.
    ///
    /// SOURCE CHANGE: all per-rivalry stats (margin, stdev, total points, upset rate,
    /// first/last played, and now Team1Wins/Team2Wins/Ties) are computed from CFBD's
    /// /teams/matchup endpoint rather than the local Games table. The local Games
    /// table only covers 1965+; several seeded rivalries (The Game 1897, Iron Bowl
    /// 1893, Red River Shootout 1900, etc.) predate that by decades, so CFBD's
    /// full-series response is now the source of truth for this method. Season win
    /// totals for upset-rate detection still come from the local TeamRecords table
    /// (see UpsetRate remarks on the MatchupHistory model for the resulting
    /// limitation on pre-1965 games).
    /// </summary>
    public class MatchupHistoryCalculator(IUnitOfWork _uow, IHttpClientFactory _httpClientFactory, ILogger<MatchupHistoryCalculator> _logger)
    {
        private HttpClient CfbdClient => _httpClientFactory.CreateClient("cfbd");

        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

        /// <summary>
        /// Calculates matchup history for all 50 curated Epic, National, State, and MEH tier rivalries.
        /// One CFBD call per rivalry (sequential, 300ms delay between calls — same rate-limit
        /// convention used by GameDataService's bulk loaders). A CFBD failure or empty response
        /// for one rivalry logs a warning and skips that rivalry; it does not abort the batch.
        /// </summary>
        public async Task<int> CalculateAllMatchupHistories(CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Starting matchup history calculation for all rivalry tiers");

            var rivalryMetadata = RivalrySeedData.GetRivalries();
            _logger.LogInformation("Found {Count} rivalries to process", rivalryMetadata.Count);

            var teamMapping = await _uow.Teams.GetAllAsync(cancellationToken);

            // Pre-load win totals for upset rate calculation — eliminates N+1 query.
            // NOTE: TeamRecords coverage starts ~1965. Games older than that will find
            // no entry here and are silently treated as "not an upset" rather than
            // failing — several rivalries below predate this coverage by decades.
            var allRecords = await _uow.TeamRecords.GetHistoricalAsync(1, 9999, cancellationToken);
            var winsLookup = allRecords
                .GroupBy(r => (r.TeamID, (int)r.Year))
                .ToDictionary(g => g.Key, g => (int)g.First().Wins);

            var matchupHistories = new List<MatchupHistory>();

            foreach (var rivalry in rivalryMetadata)
            {
                // Resolve team IDs (check both TeamName and Alias) — needed for storage
                // keys and the upset-rate winsLookup. NOT used to build the CFBD query;
                // CFBD is queried with the seed data's own name strings.
                var team1Id = teamMapping.FirstOrDefault(t =>
                    t.TeamName.Equals(rivalry.Team1Name, StringComparison.OrdinalIgnoreCase) ||
                    (t.Alias != null && t.Alias.Equals(rivalry.Team1Name, StringComparison.OrdinalIgnoreCase)))?.TeamId;

                var team2Id = teamMapping.FirstOrDefault(t =>
                    t.TeamName.Equals(rivalry.Team2Name, StringComparison.OrdinalIgnoreCase) ||
                    (t.Alias != null && t.Alias.Equals(rivalry.Team2Name, StringComparison.OrdinalIgnoreCase)))?.TeamId;

                if (team1Id == null || team2Id == null)
                {
                    _logger.LogWarning("Could not find team IDs for rivalry {RivalryName} ({Team1} vs {Team2})",
                        rivalry.RivalryName, rivalry.Team1Name, rivalry.Team2Name);
                    continue;
                }

                CfbdMatchupResponse? matchup;
                try
                {
                    var url = $"/teams/matchup?team1={Uri.EscapeDataString(rivalry.Team1Name)}&team2={Uri.EscapeDataString(rivalry.Team2Name)}";
                    var response = await CfbdClient.GetAsync(url, cancellationToken);
                    response.EnsureSuccessStatusCode();
                    matchup = await response.Content.ReadFromJsonAsync<CfbdMatchupResponse>(JsonOptions, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "CFBD matchup call failed for rivalry {RivalryName} ({Team1} vs {Team2})",
                        rivalry.RivalryName, rivalry.Team1Name, rivalry.Team2Name);
                    await Task.Delay(300, cancellationToken);
                    continue;
                }

                if (matchup == null || matchup.Games.Count == 0)
                {
                    _logger.LogWarning("No CFBD matchup games returned for rivalry {RivalryName} ({Team1} vs {Team2})",
                        rivalry.RivalryName, rivalry.Team1Name, rivalry.Team2Name);
                    await Task.Delay(300, cancellationToken);
                    continue;
                }

                // team1Id/team2Id above match rivalry.Team1Name/Team2Name, and therefore
                // match matchup.Team1/matchup.Team2 (CFBD echoes back the request params)
                // — NOT necessarily the lower/higher TeamId. Normalize storage order and
                // swap the win counts to match; otherwise Team1Wins/Team2Wins silently
                // attach to the wrong team whenever team1Id > team2Id.
                var normalizedTeam1 = Math.Min(team1Id.Value, team2Id.Value);
                var normalizedTeam2 = Math.Max(team1Id.Value, team2Id.Value);
                var team1IsNormalizedTeam1 = team1Id.Value == normalizedTeam1;

                var normalizedTeam1Wins = team1IsNormalizedTeam1 ? matchup.Team1Wins : matchup.Team2Wins;
                var normalizedTeam2Wins = team1IsNormalizedTeam1 ? matchup.Team2Wins : matchup.Team1Wins;

                // Resolve each game's home/away/winner name back to a TeamId by matching
                // against the exact strings CFBD echoed back as Team1/Team2 — more robust
                // than re-matching against Teams.TeamName/Alias, since it avoids re-tripping
                // the same "Mississippi" vs "Ole Miss" class of mismatch already found once
                // in this seed data.
                int? ResolveTeamId(string? name) =>
                    name == null ? null :
                    string.Equals(name, matchup.Team1, StringComparison.OrdinalIgnoreCase) ? team1Id :
                    string.Equals(name, matchup.Team2, StringComparison.OrdinalIgnoreCase) ? team2Id :
                    null;

                var scoredGames = matchup.Games
                    .Where(g => g.HomeScore.HasValue && g.AwayScore.HasValue)
                    .ToList();

                if (scoredGames.Count < matchup.Games.Count)
                {
                    _logger.LogWarning(
                        "{RivalryName}: {Skipped} of {Total} CFBD games had no score and were excluded from calculations",
                        rivalry.RivalryName, matchup.Games.Count - scoredGames.Count, matchup.Games.Count);
                }

                if (scoredGames.Count == 0)
                {
                    _logger.LogWarning("No scored games available for rivalry {RivalryName} after filtering", rivalry.RivalryName);
                    await Task.Delay(300, cancellationToken);
                    continue;
                }

                // Average margin and standard deviation
                var margins = scoredGames
                    .Select(g => Math.Abs((decimal)(g.HomeScore!.Value - g.AwayScore!.Value)))
                    .ToList();
                var avgMargin = margins.Average();
                var variance = margins.Sum(m => Math.Pow((double)(m - avgMargin), 2)) / margins.Count;
                var stDev = Math.Sqrt(variance);

                // Average combined total points — same games, same in-memory pass
                var avgTotalPoints = scoredGames
                    .Select(g => (decimal)(g.HomeScore!.Value + g.AwayScore!.Value))
                    .Average();

                // Upset rate: still needs local season win totals, so resolution to
                // TeamId + winsLookup happens per game rather than in a shared helper.
                var upsets = 0;
                var upsetEligibleGames = 0;
                foreach (var game in scoredGames)
                {
                    if (game.HomeScore == game.AwayScore) continue; // tie — no upset possible

                    var winnerName = game.HomeScore > game.AwayScore ? game.HomeTeam : game.AwayTeam;
                    var loserName = game.HomeScore > game.AwayScore ? game.AwayTeam : game.HomeTeam;
                    var winnerId = ResolveTeamId(winnerName);
                    var loserId = ResolveTeamId(loserName);

                    if (winnerId == null || loserId == null)
                    {
                        _logger.LogWarning(
                            "{RivalryName}: could not resolve game winner/loser to a known team ({Winner} vs {Loser}, season {Season})",
                            rivalry.RivalryName, winnerName, loserName, game.Season);
                        continue;
                    }

                    upsetEligibleGames++;
                    winsLookup.TryGetValue((winnerId.Value, game.Season), out var winnerWins);
                    winsLookup.TryGetValue((loserId.Value, game.Season), out var loserWins);
                    if (loserWins > winnerWins) upsets++;
                }
                var upsetRate = upsetEligibleGames > 0 ? (double)upsets / upsetEligibleGames : 0.0;

                // CFBD's StartYear/EndYear come back 0 in practice even when Games[]
                // itself is fully populated (confirmed on Texas/Oklahoma — 118 games
                // deserialized correctly, StartYear/EndYear did not). Derive first/last
                // played from the games we actually have instead of trusting those two
                // fields. Uses the full game list (not just scoredGames) since series
                // bounds shouldn't depend on whether a given game happened to carry a score.
                var firstPlayed = matchup.Games.Min(g => g.Season);
                var lastPlayed  = matchup.Games.Max(g => g.Season);

                matchupHistories.Add(new MatchupHistory
                {
                    Team1Id        = normalizedTeam1,
                    Team2Id        = normalizedTeam2,
                    GamesPlayed    = scoredGames.Count,
                    AvgMargin      = (decimal)avgMargin,
                    StDevMargin    = (decimal)stDev,
                    AvgTotalPoints = avgTotalPoints,
                    UpsetRate      = (decimal)upsetRate,
                    FirstPlayed    = firstPlayed,
                    LastPlayed     = lastPlayed,
                    RivalryName    = rivalry.RivalryName,
                    RivalryTier    = rivalry.Tier,
                    Team1Wins      = normalizedTeam1Wins,
                    Team2Wins      = normalizedTeam2Wins,
                    Ties           = matchup.Ties
                });

                // CFBD rate limiting — same 300ms convention used elsewhere for sequential calls.
                await Task.Delay(300, cancellationToken);
            }

            // Clear existing and insert new — same pattern as ClearAvgScoreDeltasAsync
            await _uow.Lookups.ClearMatchupHistoriesAsync(cancellationToken);
            await _uow.Lookups.AddMatchupHistoriesAsync(matchupHistories, cancellationToken);
            var saved = await _uow.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Saved {Count} matchup histories to database", matchupHistories.Count);
            return matchupHistories.Count;
        }
    }
}
