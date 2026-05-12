using HtmlAgilityPack;
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
        public async Task<List<Game>> ExtractGameDataHistoryAsync(int? year)
        {
            var gameDataList = new List<Game>();
            var httpClient   = _httpClientFactory.CreateClient();
            var tasks        = new List<Task<List<Game>>>();
            var currentYear  = DateTime.Now.Month < 8 ? DateTime.Now.Year - 1 : DateTime.Now.Year;
            var getYear      = year ?? currentYear;

            // Load team name→id dictionary once for all parallel scrape tasks
            var teamsByName = await _uow.Teams.GetTeamDictionaryByNameAsync();
            var teams = teamsByName.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.TeamID);

            while (getYear <= currentYear)
            {
                string url = $"https://www.sports-reference.com/cfb/years/{getYear}-schedule.html";
                tasks.Add(ExtractGameDataForSingleYearAsync(httpClient, url, teams, getYear));
                getYear++;
            }

            var results = await Task.WhenAll(tasks);
            foreach (var result in results)
                gameDataList.AddRange(result);

            if (gameDataList.Count > 0)
            {
                try
                {
                    await _uow.Games.AddRangeAsync(gameDataList);
                    await _uow.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error saving games to database: {ex.Message}");
                }
            }

            return gameDataList;
        }

        /// <summary>
        /// Scrapes a single year's schedule page. Takes a team name dictionary instead of
        /// a raw context so it remains stateless and safe for parallel execution.
        /// </summary>
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
            bool usedLocalFile  = false;
            string? actualFileUsed = null;

            var teamsByName = await _uow.Teams.GetTeamDictionaryByNameAsync(token);
            var teams = teamsByName.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.TeamID);

            try
            {
                using var httpClient = new HttpClient();
                string url = $"https://www.sports-reference.com/cfb/years/{year}-schedule.html";
                scrapedGames = await ExtractGameDataForSingleYearAsync(httpClient, url, teams, year);
                Console.WriteLine($"Successfully scraped data from web for year {year}");
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"Web scraping failed: {ex.Message}");

                var dataDirectory    = _configuration.GetValue<string>("CustomSettings:FilePath", "NCAA Raw Game Data");
                var requestedFilePath = Path.Combine(dataDirectory, $"{year}.txt");
                string? fileToUse   = null;

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
                        var allTeams = await _uow.Teams.GetAllAsync(token);
                        scrapedGames = new List<Game>();
                        await foreach (var recordInfo in ReadRecordsFromFileAsync(fileToUse, token))
                        {
                            if (recordInfo.Fields.Length >= 9)
                                scrapedGames.Add(recordInfo.Fields.ToGame(recordInfo.FileName, allTeams));
                        }
                        usedLocalFile  = true;
                        actualFileUsed = Path.GetFileName(fileToUse);
                    }
                    catch (Exception fileEx)
                    {
                        throw new InvalidOperationException(
                            $"Web scraping failed and local file read failed. Web error: {ex.Message}, File error: {fileEx.Message}", ex);
                    }
                }
                else
                {
                    throw new InvalidOperationException(
                        $"Web scraping failed and no local data files found. Error: {ex.Message}", ex);
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

                var existing = await _uow.Games.GetByYearAndWeekAsync(year, week, token);
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
                    await _uow.Games.AddRangeAsync(toAdd, token);

                await _uow.SaveChangesAsync(token);

                var source = usedLocalFile ? $"local file ({actualFileUsed})" : "web scraping";
                Console.WriteLine($"Processed {year}-W{week} from {source}: added {added}, updated {updated}");
                return added + updated;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving games to database: {ex.Message}");
                throw;
            }
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
                var allTeams  = await _uow.Teams.GetAllAsync(token);
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

                var existing     = await _uow.Games.GetByYearAndWeekAsync(year, week, token);
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
                    await _uow.Games.AddRangeAsync(toAdd, token);

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
                var team       = await _uow.Teams.GetByIdAsync(teamId, token);

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

                var allGames  = await _uow.Games.GetByYearAsync(year, token);
                var teamGames = allGames
                    .Where(g => g.WinnerId == teamId || g.LoserId == teamId)
                    .OrderBy(g => g.Week)
                    .ToList();

                var allTeams = await _uow.Teams.GetTeamDictionaryAsync(token);

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

        Task<int> IGameDataService.LoadTeamDataFromFile()
            => throw new NotImplementedException();
    }
}
