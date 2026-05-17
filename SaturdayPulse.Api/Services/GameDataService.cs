using HtmlAgilityPack;
using SaturdayPulse.Api.Contracts.Responses;
using SaturdayPulse.Contracts;
using SaturdayPulse.Extensions;
using SaturdayPulse.Interfaces;
using SaturdayPulse.Models;
using SaturdayPulse.ModelViews;
using SaturdayPulse.Utilities;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SaturdayPulse.Services
{
    public class GameDataService(
        IUnitOfWork _uow,
        RecordProcessor _recordProcessor,
        ScoreDeltaCalculator _scoreDeltaCalc,
        IConfiguration _configuration,
        IHttpClientFactory _httpClientFactory) : IGameDataService
    {
        // Resolved once and reused — named client carries the bearer token
        private HttpClient CfbdClient => _httpClientFactory.CreateClient("cfbd");

        #region CFBD V2 — Load Methods
        /// <summary>
        /// Bulk load — fetches teams for every year from startYear to current.
        /// Teams change conference each year so we refresh annually.
        /// </summary>
        public async Task<int> LoadTeamsBulkAsync(int startYear, CancellationToken token = default)
        {
            var currentYear = DateTime.Now.Month < 8 ? DateTime.Now.Year - 1 : DateTime.Now.Year;
            var total = 0;

            for (var year = startYear; year <= currentYear; year++)
            {
                total += await LoadTeamsAsync(year, token);
                await Task.Delay(300, token);
            }

            Console.WriteLine($"LoadTeamsBulkAsync: {total} total team upserts from {startYear} to {currentYear}");
            return total;
        }

        /// <summary>
        /// Backfills TeamsConferenceHistory by replaying /teams?year={year} for each year
        /// in the range and recording conference changes per team.
        ///
        /// Algorithm per team per year:
        ///   - Find the open row (EndYear == null) for this team
        ///   - If conference unchanged → do nothing
        ///   - If conference changed → close the open row (EndYear = year - 1),
        ///     open a new row (StartYear = year, EndYear = null)
        ///   - If no row exists → open a new row
        ///
        /// Safe to re-run — will not duplicate rows, only fills gaps.
        /// Example: POST /api/developer/buildTeamsConferenceHistory?startYear=2000
        /// </summary>
        public async Task<int> BuildTeamsConferenceHistoryAsync(int startYear, CancellationToken token = default)
        {
            var currentYear = DateTime.Now.Month < 8 ? DateTime.Now.Year - 1 : DateTime.Now.Year;
            var changes = 0;

            // Build conference name → ConferenceId lookup once
            var confByName = (await _uow.Conferences.GetAllAsync(token))
                .ToDictionary(c => c.Name, c => c.ConferenceId, StringComparer.OrdinalIgnoreCase);

            for (var year = startYear; year <= currentYear; year++)
            {
                // Fetch teams for this year from CFBD
                var response = await CfbdClient.GetAsync($"/teams?year={year}", token);
                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"BuildTeamsConferenceHistoryAsync: CFBD returned {response.StatusCode} for {year}, skipping");
                    await Task.Delay(300, token);
                    continue;
                }

                var dtos = await response.Content
                    .ReadFromJsonAsync<List<CfbdTeamV2Dto>>(cancellationToken: token) ?? [];

                // Filter to FBS — log any with unrecognized conference names
                var fbsDtos = dtos
                    .Where(d => string.Equals(d.Classification, "fbs", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var dto in fbsDtos.Where(d => d.Conference == null || !confByName.ContainsKey(d.Conference)))
                    Console.WriteLine($"BuildTeamsConferenceHistoryAsync: unmatched conference '{dto.Conference ?? "null"}' for team '{dto.School}' in {year}");

                fbsDtos = fbsDtos
                    .Where(d => d.Conference != null && confByName.ContainsKey(d.Conference))
                    .ToList();

                foreach (var dto in fbsDtos)
                {
                    var confId = confByName[dto.Conference!];

                    // Load this team's full history to find the open row
                    var history = await _uow.TeamsConferenceHistory.GetByTeamIdAsync(dto.Id, token);
                    var openRow = history.FirstOrDefault(h => h.EndYear == null);

                    if (openRow == null)
                    {
                        // No history at all — open a new row
                        await _uow.TeamsConferenceHistory.AddAsync(new TeamsConferenceHistory
                        {
                            TeamId = dto.Id,
                            ConferenceId = confId,
                            StartYear = year,
                            EndYear = null
                        }, token);
                        changes++;
                    }
                    else if (openRow.ConferenceId != confId)
                    {
                        // Conference changed — close the old row, open a new one
                        openRow.EndYear = year - 1;
                        await _uow.TeamsConferenceHistory.UpdateAsync(openRow, token);

                        await _uow.TeamsConferenceHistory.AddAsync(new TeamsConferenceHistory
                        {
                            TeamId = dto.Id,
                            ConferenceId = confId,
                            StartYear = year,
                            EndYear = null
                        }, token);
                        changes++;
                    }
                    // else: same conference, nothing to do
                }

                Console.WriteLine($"BuildTeamsConferenceHistoryAsync: completed {year}, {fbsDtos.Count} teams processed");
                await Task.Delay(300, token);
            }

            Console.WriteLine($"BuildTeamsConferenceHistoryAsync: {changes} conference changes recorded from {startYear} to {currentYear}");
            return changes;
        }


        /// <summary>
        /// Bulk load — fetches all games for every year from startYear to current.
        /// Sequential with 300ms delay per CFBD request guidelines.
        /// </summary>
        public async Task<int> LoadGamesBulkAsync(int startYear, CancellationToken token = default)
        {
            var currentYear = DateTime.Now.Month < 8 ? DateTime.Now.Year - 1 : DateTime.Now.Year;
            var total = 0;

            for (var year = startYear; year <= currentYear; year++)
            {
                total += await LoadGamesAsync(year, week: null, token);
                await Task.Delay(300, token);
            }

            Console.WriteLine($"LoadGamesBulkAsync: {total} total game upserts from {startYear} to {currentYear}");
            return total;
        }

        /// <summary>
        /// Bulk load — fetches lines for every week of every year from startYear to current.
        /// Two delays: 300ms between weeks, 500ms between years.
        /// Lines only exist from ~2013 forward so early years will return empty gracefully.
        /// </summary>
        public async Task<int> LoadLinesBulkAsync(int startYear, CancellationToken token = default)
        {
            var currentYear = DateTime.Now.Month < 8 ? DateTime.Now.Year - 1 : DateTime.Now.Year;
            var total = 0;

            for (var year = startYear; year <= currentYear; year++)
            {
                // Fetch week range from Games table so we only request weeks that exist
                var weeks = (await _uow.GamesV2.GetByYearAsync(year, token))
                    .Select(g => g.Week)
                    .Distinct()
                    .OrderBy(w => w)
                    .ToList();

                foreach (var week in weeks)
                {
                    total += await LoadLinesAsync(year, week, token);
                    await Task.Delay(300, token);
                }

                Console.WriteLine($"LoadLinesBulkAsync: completed {year}");
                await Task.Delay(500, token);
            }

            Console.WriteLine($"LoadLinesBulkAsync: {total} total lines from {startYear} to {currentYear}");
            return total;
        }



        /// <summary>
        /// Fetches all conferences from CFBD and upserts into Conferences table.
        /// </summary>
        public async Task<int> LoadConferencesAsync(CancellationToken token = default)
        {
            var response = await CfbdClient.GetAsync("/conferences", token);
            response.EnsureSuccessStatusCode();

            var dtos = await response.Content
                .ReadFromJsonAsync<List<CfbdConferenceDto>>(cancellationToken: token) ?? [];

            var conferences = dtos.Select(d => new Conference
            {
                ConferenceId   = d.Id,
                Name           = d.Name,
                ShortName      = d.ShortName,
                Abbreviation   = d.Abbreviation,
                Classification = d.Classification
            }).ToList();

            await _uow.Conferences.UpsertRangeAsync(conferences, token);
            await _uow.SaveChangesAsync(token);

            Console.WriteLine($"LoadConferencesAsync: upserted {conferences.Count} conferences");
            return conferences.Count;
        }

        /// <summary>
        /// Fetches all teams for a given year from CFBD and upserts into Teams table.
        /// Resolves ConferenceId by matching conference name against Conferences table.
        /// </summary>
        public async Task<int> LoadTeamsAsync(int? year = null, CancellationToken token = default)
        {
            var targetYear = year ?? (DateTime.Now.Month < 8 ? DateTime.Now.Year - 1 : DateTime.Now.Year);

            var response = await CfbdClient.GetAsync($"/teams?year={targetYear}", token);
            response.EnsureSuccessStatusCode();

            var dtos = await response.Content
                .ReadFromJsonAsync<List<CfbdTeamV2Dto>>(cancellationToken: token) ?? [];

            // Build conference lookup by name for FK resolution
            var conferenceLookup = (await _uow.Conferences.GetAllAsync(token))
                .ToDictionary(c => c.Name, c => c.ConferenceId, StringComparer.OrdinalIgnoreCase);

            var teams = dtos.Select(d => new Teams
            {
                TeamId       = d.Id,
                TeamName     = d.School,
                Mascot       = d.Mascot,
                Abbreviation = d.Abbreviation,
                Alias        = d.AlternateNames != null ? string.Join(",", d.AlternateNames) : null,
                Division     = d.Classification,
                ConferenceId = d.Conference != null && conferenceLookup.TryGetValue(d.Conference, out var confId)
                               ? confId : null,
                ShortName    = null  // not in /teams endpoint
            }).ToList();

            await _uow.TeamsV2.UpsertRangeAsync(teams, token);
            await _uow.SaveChangesAsync(token);

            Console.WriteLine($"LoadTeamsAsync: upserted {teams.Count} teams for {targetYear}");
            return teams.Count;
        }

        /// <summary>
        /// Fetches games for a given year (and optionally week) from CFBD and upserts into Games table.
        /// Pass week=null to load a full season sequentially with delay to avoid rate limiting.
        /// </summary>
        public async Task<int> LoadGamesAsync(int year, int? week = null, CancellationToken token = default)
        {
            var url = $"/games?year={year}&seasonType=both";
            if (week.HasValue)
                url += $"&week={week.Value}";

            var response = await CfbdClient.GetAsync(url, token);
            response.EnsureSuccessStatusCode();

            var dtos = await response.Content
                .ReadFromJsonAsync<List<CfbdGameV2Dto>>(cancellationToken: token) ?? [];

            var games = dtos.Select(d => new Games
            {
                GameId         = d.Id,
                Year           = d.Season,
                Week           = d.Week,
                SeasonType     = d.SeasonType,
                GameDate       = d.StartDate != null
                                 ? DateTime.TryParse(d.StartDate, out var dt)
                                   ? dt.ToString("MM/dd/yyyy") : d.StartDate
                                 : null,
                GameDay        = d.StartDate != null && DateTime.TryParse(d.StartDate, out var gd)
                                 ? gd.DayOfWeek.ToString()[..3].ToUpper() : null,
                HomeId         = d.HomeId,
                HomeName       = d.HomeTeam,
                HomePoints     = d.HomePoints,
                AwayId         = d.AwayId,
                AwayName       = d.AwayTeam,
                AwayPoints     = d.AwayPoints,
                NeutralSite    = d.NeutralSite,
                ConferenceGame = d.ConferenceGame,
                Attendance     = d.Attendance,
                Venue          = d.Venue
            }).ToList();

            await _uow.GamesV2.UpsertRangeAsync(games, token);
            await _uow.SaveChangesAsync(token);

            Console.WriteLine($"LoadGamesAsync: upserted {games.Count} games for {year}" +
                              (week.HasValue ? $" week {week}" : " (full season)"));
            return games.Count;
        }

        /// <summary>
        /// Fetches Vegas lines for a given year and week from CFBD.
        /// Deletes existing lines for each game before inserting fresh ones
        /// so each weekly refresh gets clean data.
        /// </summary>
        public async Task<int> LoadLinesAsync(int year, int week, CancellationToken token = default)
        {
            var url = $"/lines?year={year}&week={week}&seasonType=both";

            var response = await CfbdClient.GetAsync(url, token);
            response.EnsureSuccessStatusCode();

            var dtos = await response.Content
                .ReadFromJsonAsync<List<CfbdLinesGameDto>>(cancellationToken: token) ?? [];

            var allLines = new List<Lines>();

            foreach (var gameDto in dtos)
            {
                // Delete existing lines for this game so refresh is always clean
                await _uow.Lines.DeleteByGameIdAsync(gameDto.Id, token);

                foreach (var line in gameDto.Lines)
                {
                    // Normalize provider name — handle "Draft Kings" / "DraftKings" typo
                    var provider = line.Provider.Replace(" ", string.Empty);

                    allLines.Add(new Lines
                    {
                        GameId          = gameDto.Id,
                        Provider        = provider,
                        Spread          = line.Spread,
                        SpreadOpen      = line.SpreadOpen,
                        FormattedSpread = line.FormattedSpread,
                        OverUnder       = line.OverUnder,
                        OverUnderOpen   = line.OverUnderOpen,
                        HomeMoneyline   = line.HomeMoneyline,
                        AwayMoneyline   = line.AwayMoneyline
                    });
                }
            }

            await _uow.Lines.AddRangeAsync(allLines, token);
            await _uow.SaveChangesAsync(token);

            Console.WriteLine($"LoadLinesAsync: inserted {allLines.Count} lines across {dtos.Count} games for {year} week {week}");
            return allLines.Count;
        }

        /// <summary>
        /// Sunday / Wednesday refresh — loads games and lines for the given week.
        /// Conferences and Teams are stable enough to load on demand or at season start.
        /// </summary>
        public async Task<int> WeeklyRefreshAsync(int year, int week, CancellationToken token = default)
        {
            var gamesLoaded = await LoadGamesAsync(year, week, token);
            var linesLoaded = await LoadLinesAsync(year, week, token);
            Console.WriteLine($"WeeklyRefreshAsync: {year} week {week} — {gamesLoaded} games, {linesLoaded} lines");
            return gamesLoaded + linesLoaded;
        }

        #endregion

        #region Legacy — Original load methods (keep until V2 is validated)

        public async Task<List<Game>> ExtractGameDataHistoryAsync(int? year)
        {
            var gameDataList = new List<Game>();
            var tasks = new List<Task<List<Game>>>();
            var currentYear = DateTime.Now.Month < 8 ? DateTime.Now.Year - 1 : DateTime.Now.Year;
            var getYear = year ?? currentYear;

            var teamsByName = await _uow.Team.GetTeamDictionaryByNameAsync();
            var teamsById = (await _uow.Team.GetTeamDictionaryAsync())
                                  .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            while (getYear <= currentYear)
            {
                tasks.Add(FetchGameDataForYearAsync(CfbdClient, getYear, teamsByName, teamsById));
                getYear++;
            }

            var results = await Task.WhenAll(tasks);
            foreach (var result in results)
                gameDataList.AddRange(result);

            if (gameDataList.Count > 0)
            {
                try
                {
                    await _uow.Game.AddRangeAsync(gameDataList);
                    await _uow.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error saving games to database: {ex.Message}");
                }
            }

            return gameDataList;
        }

        private static async Task<List<Game>> ExtractGameDataForSingleYearAsync(
            HttpClient httpClient,
            string url,
            Dictionary<string, int> teamsByName,
            int year)
        {
            var gameDataList = new List<Game>();

            try
            {
                var html         = await httpClient.GetStringAsync(url);
                var htmlDocument = new HtmlDocument();
                htmlDocument.LoadHtml(html);

                var table = htmlDocument.DocumentNode.SelectSingleNode("//table[@id='schedule']");
                if (table == null) { Console.WriteLine($"Schedule table not found for year {year}."); return gameDataList; }

                var rows = table.SelectNodes(".//tbody/tr");
                if (rows == null) { Console.WriteLine($"No rows found for year {year}."); return gameDataList; }

                var regex = @"\(\d+\)&nbsp;";
                foreach (var row in rows)
                {
                    if (row.GetAttributeValue("class", "").Contains("thead")) continue;

                    var cells = row.SelectNodes(".//td");
                    if (cells == null || cells.Count < 10) continue;

                    var winnerName     = Regex.Replace(cells[4].InnerText, regex, "").Trim();
                    var loserName      = Regex.Replace(cells[7].InnerText, regex, "").Trim();
                    var siteCellText   = cells[6].InnerText.Trim();
                    char siteIndicator = siteCellText.Contains('@') ? 'L' : siteCellText.Contains('N') ? 'N' : 'W';

                    gameDataList.Add(new Game
                    {
                        Week      = int.TryParse(cells[0].InnerText.Trim(), out int w) ? w : 0,
                        WinnerId  = teamsByName.GetValueOrDefault(winnerName, -1),
                        WinnerName = winnerName,
                        WPoints   = int.TryParse(cells[5].InnerText.Trim(), out int wp) ? wp : 0,
                        Location  = siteIndicator,
                        LoserId   = teamsByName.GetValueOrDefault(loserName, -1),
                        LoserName = loserName,
                        LPoints   = int.TryParse(cells[8].InnerText.Trim(), out int lp) ? lp : 0,
                        Year      = year
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching data for year {year}: {ex.Message}");
            }

            return gameDataList;
        }

        public async Task<int> UpdateGameDataForYearAndWeekAsync(int year, int week, CancellationToken token = default)
        {
            List<Game> scrapedGames;
            bool usedLocalFile = false;
            string? actualFileUsed = null;

            var teamsByName = await _uow.Team.GetTeamDictionaryByNameAsync(token);
            var teamsById = (await _uow.Team.GetTeamDictionaryAsync(token))
                                  .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            try
            {
                scrapedGames = await FetchGameDataForYearAsync(CfbdClient, year, teamsByName, teamsById);
                Console.WriteLine($"Successfully fetched {scrapedGames.Count} games from CFBD for year {year}");
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"CFBD API fetch failed: {ex.Message}");

                var dataDirectory = _configuration.GetValue<string>("CustomSettings:FilePath", "NCAA Raw Game Data");
                var requestedFilePath = Path.Combine(dataDirectory, $"{year}.txt");
                string? fileToUse = null;

                if (File.Exists(requestedFilePath))
                {
                    fileToUse = requestedFilePath;
                }
                else if (Directory.Exists(dataDirectory))
                {
                    fileToUse = Directory.GetFiles(dataDirectory, "*.txt")
                        .Select(f => new { Path = f, Year = int.TryParse(Path.GetFileNameWithoutExtension(f), out int y) ? y : (int?)null })
                        .Where(f => f.Year.HasValue)
                        .OrderByDescending(f => f.Year)
                        .FirstOrDefault()?.Path;
                }

                if (!string.IsNullOrEmpty(fileToUse))
                {
                    try
                    {
                        var allTeams = await _uow.Team.GetAllAsync(token);
                        scrapedGames = new List<Game>();
                        await foreach (var recordInfo in ReadRecordsFromFileAsync(fileToUse, token))
                        {
                            if (recordInfo.Fields.Length >= 9)
                                scrapedGames.Add(recordInfo.Fields.ToGame(recordInfo.FileName, allTeams));
                        }
                        usedLocalFile = true;
                        actualFileUsed = Path.GetFileName(fileToUse);
                    }
                    catch (Exception fileEx)
                    {
                        throw new InvalidOperationException(
                            $"CFBD fetch failed and local file read failed. API error: {ex.Message}, File error: {fileEx.Message}", ex);
                    }
                }
                else
                {
                    throw new InvalidOperationException(
                        $"CFBD fetch failed and no local data files found. Error: {ex.Message}", ex);
                }
            }
            catch (Exception ex) when (ex is not InvalidOperationException)
            {
                Console.WriteLine($"Unexpected error during data fetch: {ex.Message}");
                throw;
            }

            try
            {
                var weekGames = scrapedGames.Where(g => g.Week == week).ToList();

                if (weekGames.Count == 0)
                {
                    Console.WriteLine($"No games found for year {year}, week {week}.");
                    return 0;
                }

                var existing = await _uow.Game.GetByYearAndWeekAsync(year, week, token);
                var existingDict = existing.ToDictionary(g => (g.WinnerId, g.LoserId));

                int updated = 0, added = 0;
                var toAdd = new List<Game>();

                foreach (var game in weekGames)
                {
                    var key = (game.WinnerId, game.LoserId);
                    if (existingDict.TryGetValue(key, out var dbGame))
                    {
                        if (ShouldUpdate(dbGame, game)) { UpdateGameProperties(dbGame, game); updated++; }
                    }
                    else
                    {
                        toAdd.Add(game);
                        added++;
                    }
                }

                if (toAdd.Count > 0)
                    await _uow.Game.AddRangeAsync(toAdd, token);

                await _uow.SaveChangesAsync(token);

                var source = usedLocalFile ? $"local file ({actualFileUsed})" : "CFBD API";
                Console.WriteLine($"Processed {year}-W{week} from {source}: added {added}, updated {updated}");
                return added + updated;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving games to database: {ex.Message}");
                throw;
            }
        }

        private static async Task<List<Game>> FetchGameDataForYearAsync(
            HttpClient httpClient,
            int year,
            Dictionary<string, SaturdayPulse.Models.Team> teamsByName,
            Dictionary<int, SaturdayPulse.Models.Team> teamsById)
        {
            var gameDataList = new List<Game>();

            try
            {
                var url = $"/games?year={year}&classification=fbs&seasonType=regular";
                var response = await httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var apiGames = await response.Content
                    .ReadFromJsonAsync<List<CfbdGameDto>>()
                    ?? [];

                foreach (var g in apiGames)
                {
                    if (!g.Completed || g.HomePoints is null || g.AwayPoints is null)
                        continue;

                    bool homeWon = g.HomePoints >= g.AwayPoints;
                    var winnerName = homeWon ? g.HomeTeam : g.AwayTeam;
                    var loserName = homeWon ? g.AwayTeam : g.HomeTeam;
                    int winnerPoints = homeWon ? g.HomePoints.Value : g.AwayPoints.Value;
                    int loserPoints = homeWon ? g.AwayPoints.Value : g.HomePoints.Value;
                    int winnerCfbdId = homeWon ? g.HomeId : g.AwayId;
                    int loserCfbdId = homeWon ? g.AwayId : g.HomeId;

                    var winnerId = teamsByName.TryGetValue(winnerName, out var wt) ? wt.TeamID
                                 : teamsById.TryGetValue(winnerCfbdId, out var wt2) ? wt2.TeamID
                                 : -1;

                    var loserId = teamsByName.TryGetValue(loserName, out var lt) ? lt.TeamID
                                 : teamsById.TryGetValue(loserCfbdId, out var lt2) ? lt2.TeamID
                                 : -1;

                    char location = g.NeutralSite ? 'N' : homeWon ? 'W' : 'L';

                    gameDataList.Add(new Game
                    {
                        Year       = year,
                        Week       = g.Week,
                        WinnerId   = winnerId,
                        WinnerName = winnerName,
                        WPoints    = winnerPoints,
                        Location   = location,
                        LoserId    = loserId,
                        LoserName  = loserName,
                        LPoints    = loserPoints
                    });
                }

                Console.WriteLine($"CFBD: fetched {apiGames.Count} games, mapped {gameDataList.Count} completed for {year}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching CFBD data for year {year}: {ex.Message}");
                throw;
            }

            return gameDataList;
        }

        private static void UpdateGameProperties(Game dbGame, Game game)
        {
            dbGame.WPoints = game.WPoints;
            dbGame.LPoints = game.LPoints;
        }

        private static bool ShouldUpdate(Game dbGame, Game game)
            => dbGame.WinnerId == game.WinnerId &&
               dbGame.LoserId  == game.LoserId  &&
               game is { WPoints: 0, LPoints: 0 };

        public async Task<int> LoadGameHistoryFromFiles()
        {
            var dataDirectory = _configuration.GetValue<string>("CustomSettings:FilePath", "NCAA Raw Game Data");
            var games         = await ProcessDirectoryAsync(dataDirectory);
            await UpdateTeamRecordsAsync();
            await _scoreDeltaCalc.UpdateAvgScoreDeltasTableAsync();
            return games;
        }

        public async Task<int> ProcessDirectoryAsync(string directoryPath)
        {
            Console.WriteLine($"Starting processing in {directoryPath}...");
            var tokenSource      = new CancellationTokenSource();
            var recordsProcessed = 0;

            await Parallel.ForEachAsync(
                ReadRecordsAsync(directoryPath, tokenSource.Token),
                tokenSource.Token,
                async (recordInfo, token) =>
                {
                    await _recordProcessor.ProcessSingleRecordAsync(recordInfo.Fields, recordInfo.FileName, token);
                    recordsProcessed++;
                });

            return recordsProcessed;
        }

        public async IAsyncEnumerable<FileRecord> ReadRecordsFromFileAsync(
            string filePath, [EnumeratorCancellation] CancellationToken token = default)
        {
            string fileName = Path.GetFileNameWithoutExtension(filePath);
            await foreach (var line in File.ReadLinesAsync(filePath, token))
            {
                if (string.IsNullOrEmpty(line) || line.Trim().StartsWith("Rk"))
                    continue;

                var fields = line.Split(',').Select(f => f.Trim()).ToArray();
                yield return new FileRecord(fileName, fields);
            }
        }

        public async IAsyncEnumerable<FileRecord> ReadRecordsAsync(
            string directoryPath, [EnumeratorCancellation] CancellationToken token = default)
        {
            foreach (var filePath in Directory.EnumerateFiles(directoryPath, "*.txt"))
                await foreach (var record in ReadRecordsFromFileAsync(filePath, token))
                    yield return record;
        }

        public async Task<int> ProcessSingleFileAsync(string filePath, CancellationToken token = default)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"File not found: {filePath}");

            Console.WriteLine($"Processing file: {filePath}");
            var recordsProcessed = 0;

            await foreach (var recordInfo in ReadRecordsFromFileAsync(filePath, token))
            {
                await _recordProcessor.ProcessSingleRecordAsync(recordInfo.Fields, recordInfo.FileName, token);
                recordsProcessed++;
            }

            Console.WriteLine($"Completed processing {recordsProcessed} records from {Path.GetFileName(filePath)}");
            return recordsProcessed;
        }

        public async Task<int> UpdateGameDataFromFileAsync(
            string filePath, int year, int week, CancellationToken token = default)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"File not found: {filePath}");

            try
            {
                var allTeams  = await _uow.Team.GetAllAsync(token);
                var fileGames = new List<Game>();

                await foreach (var recordInfo in ReadRecordsFromFileAsync(filePath, token))
                    if (recordInfo.Fields.Length >= 9)
                        fileGames.Add(recordInfo.Fields.ToGame(recordInfo.FileName, allTeams));

                var weekGames = fileGames.Where(g => g.Week == week).ToList();

                if (weekGames.Count == 0)
                {
                    Console.WriteLine($"No games found for year {year}, week {week} in file {Path.GetFileName(filePath)}.");
                    return 0;
                }

                var existing     = await _uow.Game.GetByYearAndWeekAsync(year, week, token);
                var existingDict = existing.ToDictionary(g => (g.WinnerId, g.LoserId));

                int updated = 0, added = 0;
                var toAdd = new List<Game>();

                foreach (var game in weekGames)
                {
                    var key = (game.WinnerId, game.LoserId);
                    if (existingDict.TryGetValue(key, out var dbGame))
                    {
                        if (ShouldUpdate(dbGame, game)) { UpdateGameProperties(dbGame, game); updated++; }
                    }
                    else
                    {
                        toAdd.Add(game);
                        added++;
                    }
                }

                if (toAdd.Count > 0)
                    await _uow.Game.AddRangeAsync(toAdd, token);

                await _uow.SaveChangesAsync(token);

                Console.WriteLine($"Processed {year}-W{week} from file: added {added}, updated {updated}");
                return added + updated;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing file for year {year}, week {week}: {ex.Message}");
                throw;
            }
        }

        public async Task UpdateTeamRecordsAsync(int? targetYear = null, CancellationToken token = default)
        {
            try
            {
                await _uow.TeamRecords.UpsertFromGamesAsync(targetYear, token);
                await _uow.SaveChangesAsync(token);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating team records: {ex.Message}");
                if (ex.InnerException != null)
                    Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
                throw;
            }
        }

        public async Task<string> GetTeamScheduleAsJsonAsync(int teamId, int year, CancellationToken token = default)
        {
            try
            {
                var teamRecord = await _uow.TeamRecords.GetByTeamAndYearAsync(teamId, year, token);
                var team       = await _uow.Team.GetByIdAsync(teamId, token);

                var summary = teamRecord != null && team != null
                    ? new TeamSeasonSummaryView
                    {
                        Year          = teamRecord.Year,
                        TeamName      = team.TeamName,
                        Wins          = teamRecord.Wins,
                        Losses        = teamRecord.Losses,
                        PointsFor     = teamRecord.PointsFor,
                        PointsAgainst = teamRecord.PointsAgainst
                    }
                    : null;

                var allGames  = await _uow.Game.GetByYearAsync(year, token);
                var teamGames = allGames
                    .Where(g => g.WinnerId == teamId || g.LoserId == teamId)
                    .OrderBy(g => g.Week)
                    .ToList();

                var allTeams = await _uow.Team.GetTeamDictionaryAsync(token);

                var games = teamGames.Select(g =>
                {
                    bool won      = g.WinnerId == teamId;
                    var oppId     = won ? g.LoserId : g.WinnerId;
                    allTeams.TryGetValue(oppId, out var opp);

                    return new TeamGameResultView
                    {
                        Week       = g.Week,
                        Result     = won ? "Win" : "Loss",
                        Opponent   = won ? g.LoserName  : g.WinnerName,
                        Division   = opp?.Division,
                        Conference = opp?.ConferenceAbbr,
                        Score      = won
                            ? $"{g.WPoints} - {g.LPoints}"
                            : $"{g.LPoints} - {g.WPoints}"
                    };
                }).ToList();

                return JsonSerializer.Serialize(
                    new TeamScheduleResponse { Summary = summary, Games = games },
                    new JsonSerializerOptions
                    {
                        WriteIndented        = true,
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting team schedule for team {teamId}, year {year}: {ex.Message}");
                throw;
            }
        }

        public async Task<List<CfbdTeamDto>> PreviewCfbdTeamsAsync(int? year = null, CancellationToken token = default)
        {
            var targetYear = year ?? DateTime.Now.Year;
            var response = await CfbdClient.GetAsync($"/teams/fbs?year={targetYear}", token);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<List<CfbdTeamDto>>(cancellationToken: token) ?? [];
        }

        public async Task<List<CfbdGameDto>> PreviewCfbdGamesAsync(int year, int? week = null, CancellationToken token = default)
        {
            var url = $"/games?year={year}&classification=fbs&seasonType=regular";
            if (week.HasValue)
                url += $"&week={week.Value}";

            var response = await CfbdClient.GetAsync(url, token);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<List<CfbdGameDto>>(cancellationToken: token) ?? [];
        }

        Task<int> IGameDataService.LoadTeamDataFromFile()
            => throw new NotImplementedException();

        #endregion
    }
}
