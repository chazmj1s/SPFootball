using Microsoft.Maui.Devices;
using SaturdayPulse.Models;
using System.Net.Http.Json;
using System.Text.Json;

namespace SaturdayPulse.Services
{
    /// <summary>
    /// Service for calling the NCAA Power Ratings GameData API
    /// </summary>
    public class GameDataApiService
    {
        private readonly HttpClient _httpClient;

        public GameDataApiService(HttpClient httpClient)
        {
            _httpClient = httpClient;

        }

        /// <summary>
        /// Gets power rankings for a specific year
        /// </summary>
        public async Task<List<Models.TeamRanking>?> GetPowerRankingsAsync(
            int? year = null, int? throughWeek = null)
        {
            try
            {
                var currentYear = year ?? DateTime.Now.Year;

                System.Diagnostics.Debug.WriteLine($"[API] ========================================");
                System.Diagnostics.Debug.WriteLine($"[API] Fetching power rankings for year {currentYear}, week {throughWeek?.ToString() ?? "all"}");

                var url = $"powerrankings/v2?year={currentYear}";
                if (throughWeek.HasValue)
                    url += $"&throughWeek={throughWeek}";

                System.Diagnostics.Debug.WriteLine($"[API] Full URL: {url}");

                // Stream directly — avoids double allocation (string + object graph)
                using var response = await _httpClient.GetAsync(
                    url, HttpCompletionOption.ResponseHeadersRead);

                System.Diagnostics.Debug.WriteLine($"[API] Response Status: {response.StatusCode}");

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    System.Diagnostics.Debug.WriteLine($"[API] Error Response: {errorContent}");
                    throw new HttpRequestException($"API returned {response.StatusCode}");
                }

                using var stream = await response.Content.ReadAsStreamAsync();
                var options = new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                // Deserialize into lightweight DTO
                var dtos = await System.Text.Json.JsonSerializer
                    .DeserializeAsync<List<Models.TeamRankingDto>>(stream, options);

                // Map to UI-bound TeamRanking
                var rankings = dtos?.ToTeamRankings();

                if (rankings != null && rankings.Count > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[API] First team: {rankings[0].TeamName} - Rank: {rankings[0].OverallRank} ({rankings[0].Tier} #{rankings[0].TierRank}) - Power: {rankings[0].Ranking}");
                    System.Diagnostics.Debug.WriteLine($"[API] Last team: {rankings[^1].TeamName} - Rank: {rankings[^1].OverallRank}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[API] WARNING: No rankings returned from API!");
                }

                System.Diagnostics.Debug.WriteLine($"[API] ========================================");
                return rankings;
            }
            catch (HttpRequestException httpEx)
            {
                System.Diagnostics.Debug.WriteLine($"[API] HTTP ERROR: {httpEx.Message}");
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] ERROR: {ex.GetType().Name}: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[API] Stack: {ex.StackTrace}");
                return null;
            }
        }
        /// <summary>
        /// Fetches Seed / Trend / Pedigree rating history for a single team.
        /// Called lazily when the user first taps "Trend / Pedigree ▼" on a row.
        ///
        /// Maps to: GET /api/productiongamedata/rollingAverages/team?teamId=X&amp;startYear=Y
        /// </summary>
        public async Task<TeamTrendData?> GetTeamRollingAveragesAsync(int teamId, int? startYear = null)
        {
            try
            {
                var url = $"rollingAverages/team/v2?teamId={teamId}";
                if (startYear.HasValue)
                    url += $"&startYear={startYear.Value}";

                System.Diagnostics.Debug.WriteLine($"[API] Fetching rolling averages: {url}");

                var data = await _httpClient.GetFromJsonAsync<TeamTrendData>(url);

                System.Diagnostics.Debug.WriteLine(
                    $"[API] Rolling averages for teamId={teamId}: " +
                    $"{data?.History?.Count ?? 0} season(s) returned");

                return data;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[API] Error fetching rolling averages for teamId={teamId}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Gets week-by-week season arc data for a single team.
        /// Maps to: GET /api/productiongamedata/teamseason?teamId=X&year=Y
        /// </summary>
        public async Task<TeamSeasonArcResponse?> GetTeamSeasonArcAsync(int teamId, int year)
        {
            try
            {
                var url = $"teamseason?teamId={teamId}&year={year}";
                System.Diagnostics.Debug.WriteLine($"[API] Fetching season arc: {url}");
                var data = await _httpClient.GetFromJsonAsync<TeamSeasonArcResponse>(url);
                System.Diagnostics.Debug.WriteLine(
                    $"[API] Season arc for teamId={teamId}: {data?.Weeks?.Count ?? 0} week(s) returned");
                return data;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[API] Error fetching season arc for teamId={teamId}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Gets all FBS teams with id, name, conference, and tier.
        /// </summary>
        public async Task<List<Models.TeamInfo>?> GetTeamsAsync()
        {
            try
            {
                var url = $"teams/v2";
                return await _httpClient.GetFromJsonAsync<List<Models.TeamInfo>>(url);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] Error getting teams: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Gets the full season schedule with actual and projected scores.
        /// </summary>
        public async Task<List<Models.GameResult>?> GetScheduleAsync(int? year = null)
        {
            try
            {
                var currentYear = year ?? DateTime.Now.Year;
                var url = $"schedule/v2?year={currentYear}";

                // Stream directly — avoids reading entire payload into a string first
                using var response = await _httpClient.GetAsync(
                    url, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                using var stream = await response.Content.ReadAsStreamAsync();
                var options = new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                // Deserialize into lightweight DTO — no INPC, no computed properties
                var dtos = await System.Text.Json.JsonSerializer
                    .DeserializeAsync<List<Models.GameResultDto>>(stream, options);

                // Map to UI-bound GameResult on whatever thread we're on (caller wraps in Task.Run)
                return dtos?.ToGameResults();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] Error getting schedule: {ex.Message}");
                return null;
            }
        }


        /// <summary>
        /// Returns the conferences active in a given year, ordered P4 → G5.
        /// Used to populate the conference filter dropdown on year change.
        /// Maps to: GET /conferences/{year}
        /// </summary>
        public async Task<List<Models.ConferenceInfo>?> GetConferencesForYearAsync(int year)
        {
            try
            {
                var url = $"conferences/{year}";
                System.Diagnostics.Debug.WriteLine($"[API] Fetching conferences for year {year}");
                var result = await _httpClient.GetFromJsonAsync<List<Models.ConferenceInfo>>(url);
                System.Diagnostics.Debug.WriteLine($"[API] Conferences for {year}: {result?.Count ?? 0} returned");
                return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] Error fetching conferences for year {year}: {ex.Message}");
                return null;
            }
        }

        public async Task<List<PlayedWeekInfo>> GetPlayedWeeksByYear(int year)
        {
            try
            {
                var url = $"weeks/{year}";
                var result = await _httpClient.GetFromJsonAsync<List<PlayedWeekInfo>>(url);

                return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] Error fetching game weeks for year {year}: {ex.Message}");
                return null;
            }
        }

        // <summary>
        /// Gets the full season schedule with actual and projected scores.
        /// </summary>
        public async Task<List<Models.GameResult>?> GetPostseasonAsync(int? year = null)
        {
            try
            {
                var currentYear = year ?? DateTime.Now.Year;
                var url = $"postseason/v2?year={currentYear}";
                var envelope = await _httpClient.GetFromJsonAsync<PostseasonEnvelope>(url);
                return envelope?.Games;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] Error getting postseason: {ex.Message}");
                return null;
            }
        }

        private class PostseasonEnvelope
        {
            public List<Models.GameResult> Games { get; set; } = new();
        }

        /// <summary>
        /// Gets team schedule as JSON
        /// </summary>
        public async Task<TeamScheduleResponse?> GetTeamScheduleAsync(int teamId, int year)
        {
            try
            {
                var url = $"teamSchedule/v2?teamId={teamId}&year={year}";
                return await _httpClient.GetFromJsonAsync<TeamScheduleResponse>(url);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting team schedule: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Gets CFP playoff games (SeasonType == "playoff") for a given year.
        /// </summary>
        public async Task<List<Models.GameResult>?> GetPlayoffGamesAsync(int? year = null)
        {
            try
            {
                var currentYear = year ?? DateTime.Now.Year;
                var url = $"playoff-games/v2?year={currentYear}";
                return await _httpClient.GetFromJsonAsync<List<Models.GameResult>>(url);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] Error getting playoff games: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Gets FBS bowl games (SeasonType == "postseason") for a given year.
        /// </summary>
        public async Task<List<Models.GameResult>?> GetBowlGamesAsync(int? year = null)
        {
            try
            {
                var currentYear = year ?? DateTime.Now.Year;
                var url = $"bowl-games/v2?year={currentYear}";
                return await _httpClient.GetFromJsonAsync<List<Models.GameResult>>(url);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] Error getting bowl games: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Sandbox: predicts a matchup between two teams from potentially different years.
        /// </summary>
        public async Task<Models.SandboxPrediction?> GetSandboxPredictionAsync(
            string teamName, int teamYear, string opponentName, int opponentYear)
        {
            try
            {
                var url = $"sandbox/predict?teamName={Uri.EscapeDataString(teamName)}&teamYear={teamYear}" +
                          $"&opponentName={Uri.EscapeDataString(opponentName)}&opponentYear={opponentYear}";
                return await _httpClient.GetFromJsonAsync<Models.SandboxPrediction>(url);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] Sandbox predict error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Returns the years a team has WeeklyRankings data for — used by sandbox year picker.
        /// </summary>
        public async Task<List<int>?> GetTeamAvailableYearsAsync(int teamId)
        {
            try
            {
                return await _httpClient.GetFromJsonAsync<List<int>>(
                    $"sandbox/team-years?teamId={teamId}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] Team years error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Gets all named rivalries with series metadata.
        /// </summary>
        public async Task<List<Models.RivalryInfo>?> GetNamedRivalriesAsync()
        {
            try
            {
                var url = $"rivalries/named/v2";
                return await _httpClient.GetFromJsonAsync<List<Models.RivalryInfo>>(url);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] Error getting named rivalries: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Gets head-to-head history and series metadata for any two teams.
        /// Maps to GET /api/productiongamedata/rivalryhistory?team1Id=X&team2Id=Y
        /// Returns a RivalryInfo built from the response, or null on failure.
        /// </summary>
        public async Task<Models.RivalryInfo?> GetMatchupHistoryAsync(int team1Id, int team2Id)
        {
            try
            {
                var url = $"rivalryhistory/v2?team1Id={team1Id}&team2Id={team2Id}";
                var response = await _httpClient.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    System.Diagnostics.Debug.WriteLine($"[API] rivalryhistory returned {response.StatusCode} for {team1Id} vs {team2Id}");
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync();
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var r = JsonSerializer.Deserialize<JsonElement>(json, options);

                // AvgMargin and UpsetRate may be null if no MatchupHistory record exists
                decimal avgMargin  = 0m;
                decimal upsetRate  = 0m;
                decimal stDev      = 0m;

                if (r.TryGetProperty("avgMargin", out var am) && am.ValueKind != JsonValueKind.Null)
                    avgMargin = am.GetDecimal();
                if (r.TryGetProperty("upsetRate", out var ur) && ur.ValueKind != JsonValueKind.Null)
                    upsetRate = ur.GetDecimal();

                int gamesPlayed = r.TryGetProperty("gamesPlayed", out var gp)
                    ? gp.GetInt32() : 0;

                // Derive first/last played from history array if present
                int firstPlayed = 0, lastPlayed = 0;
                if (r.TryGetProperty("history", out var hist) && hist.ValueKind == JsonValueKind.Array)
                {
                    var years = hist.EnumerateArray()
                        .Select(h => h.TryGetProperty("year", out var y) ? y.GetInt32() : 0)
                        .Where(y => y > 0)
                        .ToList();
                    if (years.Count > 0)
                    {
                        firstPlayed = years.Min();
                        lastPlayed  = years.Max();
                    }
                }

                return new Models.RivalryInfo
                {
                    Team1Id        = team1Id,
                    Team1Name      = r.TryGetProperty("team1Name",      out var t1n) ? t1n.GetString() ?? string.Empty : string.Empty,
                    Team1ShortName = r.TryGetProperty("team1ShortName", out var t1s) ? t1s.GetString() ?? string.Empty : string.Empty,
                    Team2Id        = team2Id,
                    Team2Name      = r.TryGetProperty("team2Name",      out var t2n) ? t2n.GetString() ?? string.Empty : string.Empty,
                    Team2ShortName = r.TryGetProperty("team2ShortName", out var t2s) ? t2s.GetString() ?? string.Empty : string.Empty,
                    RivalryName    = r.TryGetProperty("rivalryName",    out var rn)  && rn.ValueKind != JsonValueKind.Null
                                         ? rn.GetString() : null,
                    RivalryTier    = r.TryGetProperty("rivalryTier",    out var rt)  && rt.ValueKind != JsonValueKind.Null
                                         ? rt.GetString() : "PERSONAL",
                    GamesPlayed    = gamesPlayed,
                    AvgMargin      = avgMargin,
                    StDevMargin    = stDev,
                    UpsetRate      = upsetRate,
                    FirstPlayed    = firstPlayed,
                    LastPlayed     = lastPlayed,
                    IsGameFavorited = true
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] Error getting matchup history {team1Id} vs {team2Id}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Updates game data for a specific week
        /// </summary>
        public async Task<bool> UpdateWeekGamesAsync(int year, int week)
        {
            try
            {
                var url = $"updateWeekGames?year={year}&week={week}";
                var response = await _httpClient.PostAsync(url, null);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating week games: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Gets projected conference standings for all FBS teams.
        /// </summary>
        public async Task<List<ProjectedTeamStanding>> GetProjectedStandingsAsync(
            int year,
            int? throughWeek = null)
        {
            try
            {
                var url = $"projected-standings/v2?year={year}";
                if (throughWeek.HasValue)
                    url += $"&throughWeek={throughWeek}";

                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode) return null;

                var json = await response.Content.ReadAsStringAsync();
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var raw = JsonSerializer.Deserialize<List<JsonElement>>(json, options);

                return raw?.Select(r => new ProjectedTeamStanding
                {
                    TeamName        = r.GetProperty("teamName").GetString(),
                    Conference      = r.GetProperty("conference").GetString(),
                    Division        = r.TryGetProperty("division", out var div) && div.ValueKind != JsonValueKind.Null
                                          ? div.GetString() : null,
                    ActualWins      = r.GetProperty("actualWins").GetInt32(),
                    ActualLosses    = r.GetProperty("actualLosses").GetInt32(),
                    ProjectedWins   = r.GetProperty("projectedWins").GetInt32(),
                    ProjectedLosses = r.GetProperty("projectedLosses").GetInt32(),
                    ProjectedWinPct = r.GetProperty("projectedWinPct").GetDouble(),
                    Games           = r.GetProperty("games").EnumerateArray().Select(g => new ProjectedGame
                    {
                        Week        = g.GetProperty("week").GetInt32(),
                        Opponent    = g.GetProperty("opponent").GetString(),
                        Location    = g.GetProperty("location").GetString(),
                        Result      = g.GetProperty("result").GetString(),
                        Score       = g.TryGetProperty("score",      out var sc) && sc.ValueKind != JsonValueKind.Null ? sc.GetString() : null,
                        ProjScore   = g.TryGetProperty("projScore",  out var ps) && ps.ValueKind != JsonValueKind.Null ? ps.GetString() : null,
                        Confidence  = g.TryGetProperty("confidence", out var cf) && cf.ValueKind != JsonValueKind.Null ? cf.GetString() : null,
                        Type        = g.GetProperty("type").GetString(),
                        NeutralSite = g.GetProperty("neutralSite").GetBoolean()
                    }).ToList()
                }).ToList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] Error getting projected standings: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Gets projected conference championship qualifiers for all FBS conferences.
        /// </summary>
        public async Task<List<ChampionshipMatchup>?> GetProjectedChampionshipQualifiersAsync(
            int year,
            int? throughWeek = null)
        {
            try
            {
                var url = $"projected-championship-qualifiers/v2?year={year}";
                if (throughWeek.HasValue)
                    url += $"&throughWeek={throughWeek}";

                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode) return null;

                var json = await response.Content.ReadAsStringAsync();
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var raw = JsonSerializer.Deserialize<List<JsonElement>>(json, options);

                return raw?.Select(r => new ChampionshipMatchup
                {
                    Conference       = r.GetProperty("conference").GetString(),
                    Format           = r.GetProperty("format").GetString(),
                    Qualifier1Method = r.GetProperty("qualifier1Method").GetString(),
                    Qualifier2Method = r.GetProperty("qualifier2Method").GetString(),
                    SimulatedThrough = r.TryGetProperty("simulatedThrough", out var st) ? st.GetString() : null,
                    TiebreakerLog    = r.GetProperty("tiebreakerLog").EnumerateArray()
                                           .Select(l => l.GetString()).ToList(),
                    StubsApplied     = r.GetProperty("stubsApplied").EnumerateArray()
                                           .Select(l => l.GetString()).ToList(),
                    Qualifier1       = ParseQualifier(r.GetProperty("qualifier1")),
                    Qualifier2       = ParseQualifier(r.GetProperty("qualifier2")),
                    Contenders       = r.GetProperty("contenders").EnumerateArray()
                                           .Select(c => new ChampionshipContender
                                           {
                                               TeamName         = c.GetProperty("teamName").GetString(),
                                               ConferenceWins   = c.GetProperty("conferenceWins").GetInt32(),
                                               ConferenceLosses = c.GetProperty("conferenceLosses").GetInt32(),
                                               ActualConferenceWins = c.GetProperty("actualConferenceWins").GetInt32(),
                                               ActualConferenceLosses = c.GetProperty("actualConferenceLosses").GetInt32()
                                           }).ToList()
                }).ToList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] Error getting championship qualifiers: {ex.Message}");
                return null;
            }
        }

        private static ChampionshipQualifier ParseQualifier(JsonElement q) => new()
        {
            TeamName         = q.GetProperty("teamName").GetString(),
            ConferenceWins   = q.GetProperty("conferenceWins").GetInt32(),
            ConferenceLosses = q.GetProperty("conferenceLosses").GetInt32(),
            ActualConferenceWins = q.GetProperty("actualConferenceWins").GetInt32(),
            ActualConferenceLosses = q.GetProperty("actualConferenceLosses").GetInt32(),
            Division         = q.TryGetProperty("division", out var d) && d.ValueKind != JsonValueKind.Null
                                   ? d.GetString() : null
        };
    }
}
