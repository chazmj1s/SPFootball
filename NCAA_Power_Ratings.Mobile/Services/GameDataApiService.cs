using Microsoft.Maui.Devices;
using NCAA_Power_Ratings.Mobile.Models;
using System.Net.Http.Json;
using System.Text.Json;

namespace NCAA_Power_Ratings.Mobile.Services
{
    /// <summary>
    /// Service for calling the NCAA Power Ratings GameData API
    /// </summary>
    public class GameDataApiService
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;

        public GameDataApiService(HttpClient httpClient)
        {
            _httpClient = httpClient;

            // Configure base URL based on platform for local testing
            // TODO: Update this to your deployed API URL
#if DEBUG
            _baseUrl = DeviceInfo.Platform == DevicePlatform.Android
                ? "http://10.0.2.2:5086/api/productiongamedata"
                : "http://localhost:5086/api/productiongamedata";
#else
                _baseUrl = "https://ncaa-power-ratings-api-ftdyg2bxhpfxc9an.westus2-01.azurewebsites.net/api/productionGameData";
#endif
        }

        /// <summary>
        /// Gets power rankings for a specific year
        /// </summary>
        public async Task<List<Models.TeamRanking>?> GetPowerRankingsAsync(int? year = null, int? week = null)
        {
            try
            {
                var currentYear = year ?? DateTime.Now.Year;

                System.Diagnostics.Debug.WriteLine($"[API] ========================================");
                System.Diagnostics.Debug.WriteLine($"[API] Fetching power rankings for year {currentYear}, week {week?.ToString() ?? "all"}");
                System.Diagnostics.Debug.WriteLine($"[API] Base URL: {_baseUrl}");

                var url = $"{_baseUrl}/powerrankings?year={currentYear}";
                if (week.HasValue)
                    url += $"&throughWeek={week.Value}";
                System.Diagnostics.Debug.WriteLine($"[API] Full URL: {url}");

                var response = await _httpClient.GetAsync(url);
                System.Diagnostics.Debug.WriteLine($"[API] Response Status: {response.StatusCode}");

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    System.Diagnostics.Debug.WriteLine($"[API] Error Response: {errorContent}");
                    throw new HttpRequestException($"API returned {response.StatusCode}");
                }

                var jsonContent = await response.Content.ReadAsStringAsync();
                System.Diagnostics.Debug.WriteLine($"[API] Response length: {jsonContent.Length} characters");
                System.Diagnostics.Debug.WriteLine($"[API] First 500 chars: {(jsonContent.Length > 500 ? jsonContent.Substring(0, 500) : jsonContent)}");

                var rankings = await response.Content.ReadFromJsonAsync<List<Models.TeamRanking>>();

                System.Diagnostics.Debug.WriteLine($"[API] Successfully deserialized {rankings?.Count ?? 0} rankings");

                if (rankings != null && rankings.Count > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[API] First team: {rankings[0].TeamName} - Rank: {rankings[0].OverallRank} ({rankings[0].Tier} #{rankings[0].TierRank}) - Power: {rankings[0].Ranking}");
                    System.Diagnostics.Debug.WriteLine($"[API] Last team: {rankings[^1].TeamName} - Rank: {rankings[^1].OverallRank} ({rankings[^1].Tier} #{rankings[^1].TierRank}) - Power: {rankings[^1].Ranking}");
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
                System.Diagnostics.Debug.WriteLine($"[API] ========================================");
                System.Diagnostics.Debug.WriteLine($"[API] HTTP ERROR: {httpEx.Message}");
                System.Diagnostics.Debug.WriteLine($"[API] Is the backend API running at {_baseUrl}?");
                System.Diagnostics.Debug.WriteLine($"[API] Falling back to mock data");
                System.Diagnostics.Debug.WriteLine($"[API] ========================================");

                return await GetMockPowerRankingsAsync(year ?? DateTime.Now.Year);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] ========================================");
                System.Diagnostics.Debug.WriteLine($"[API] ERROR: {ex.GetType().Name}: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[API] Stack: {ex.StackTrace}");
                System.Diagnostics.Debug.WriteLine($"[API] Falling back to mock data");
                System.Diagnostics.Debug.WriteLine($"[API] ========================================");

                return await GetMockPowerRankingsAsync(year ?? DateTime.Now.Year);
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
                var url = $"{_baseUrl}/rollingAverages/team?teamId={teamId}";
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
        /// Gets all FBS teams with id, name, conference, and tier.
        /// </summary>
        public async Task<List<Models.TeamInfo>?> GetTeamsAsync()
        {
            try
            {
                var url = $"{_baseUrl}/teams";
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
                var url = $"{_baseUrl}/schedule?year={currentYear}";
                var schedule = await _httpClient.GetFromJsonAsync<List<Models.GameResult>>(url);
                return schedule;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[API] Error getting schedule: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Gets team schedule as JSON
        /// </summary>
        public async Task<string?> GetTeamScheduleAsync(int teamId, int year)
        {
            try
            {
                var url = $"{_baseUrl}/teamSchedule?teamId={teamId}&year={year}";
                var response = await _httpClient.GetStringAsync(url);
                return response;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting team schedule: {ex.Message}");
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
                var url = $"{_baseUrl}/rivalries/named";
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
                var url = $"{_baseUrl}/rivalryhistory?team1Id={team1Id}&team2Id={team2Id}";
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
                    IsPersonalFollowed = true
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
                var url = $"{_baseUrl}/updateWeekGames?year={year}&week={week}";
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
        /// Mock data for testing UI before backend endpoint is ready
        /// </summary>
        private async Task<List<Models.TeamRanking>> GetMockPowerRankingsAsync(int year)
        {
            await Task.Delay(500);

            var conferences = new[] { "SEC", "Big Ten", "Big 12", "ACC", "Pac-12" };
            var teams = new List<Models.TeamRanking>();
            var random = new Random(42);

            for (int i = 1; i <= 133; i++)
            {
                teams.Add(new Models.TeamRanking
                {
                    TeamID         = i,
                    TeamName       = $"Team {i}",
                    Conference     = conferences[i % conferences.Length],
                    ConferenceAbbr = conferences[i % conferences.Length],
                    Division       = "FBS",
                    OverallRank    = i,
                    Ranking        = (decimal)(100 - (i * 0.5) + random.NextDouble() * 5),
                    Year           = year,
                    Wins           = (byte)random.Next(0, 13),
                    Losses         = (byte)random.Next(0, 6),
                    BaseSOS        = (decimal)(random.NextDouble() * 10),
                    CombinedSOS    = (decimal)(random.NextDouble() * 15)
                });
            }

            return teams;
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
                var url = $"{_baseUrl}/projected-standings?year={year}";
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
        public async Task<List<ChampionshipMatchup>> GetProjectedChampionshipQualifiersAsync(
            int year,
            int? throughWeek = null)
        {
            try
            {
                var url = $"{_baseUrl}/projected-championship-qualifiers?year={year}";
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
                                               ConferenceLosses = c.GetProperty("conferenceLosses").GetInt32()
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
            OverallWins      = q.GetProperty("overallWins").GetInt32(),
            OverallLosses    = q.GetProperty("overallLosses").GetInt32(),
            Division         = q.TryGetProperty("division", out var d) && d.ValueKind != JsonValueKind.Null
                                   ? d.GetString() : null
        };
    }
}
