using HtmlAgilityPack;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SaturdayPulse.Contracts.Responses;
using SaturdayPulse.Contracts;
using SaturdayPulse.Interfaces;
using SaturdayPulse.Models;
using SaturdayPulse.ModelViews;
using SaturdayPulse.Utilities;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using SaturdayPulse.Api.Contracts.Responses;

namespace SaturdayPulse.Services
{
    public class GameDataService(
        IUnitOfWork _uow,
        IHttpClientFactory _httpClientFactory) : IGameDataService
    {
        // Resolved once and reused — named client carries the bearer token
        private HttpClient CfbdClient => _httpClientFactory.CreateClient("cfbd");

        #region CFBD V2 — Load Methods


        /// <summary>
        /// Loads transfer portal entries for a single season from CFBD.
        /// Filters out Withdrawn entries before persisting.
        /// Only FBS-relevant transfers are stored — destination or origin must be an FBS team.
        /// </summary>
        public async Task<int> LoadPortalAsync(int season, CancellationToken token = default)
        {
            // CFBD endpoint: GET /transferPortal?year={season}
            var response = await CfbdClient.GetAsync($"player/portal?year={season}", token);
            response.EnsureSuccessStatusCode();

            var entries = await System.Text.Json.JsonSerializer.DeserializeAsync<List<CfbdPortalEntry>>(
                await response.Content.ReadAsStreamAsync(token),
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true },
                token);
            if (entries == null || entries.Count == 0) return 0;

            // Load FBS team names for filtering.
            var fbsTeams = await _uow.Teams.GetAllAsync(token);
            var fbsNames = fbsTeams
                .Where(t => string.Equals(t.Division, "fbs", StringComparison.OrdinalIgnoreCase))
                .Select(t => t.TeamName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Also include known aliases.
            var aliasNames = fbsTeams
                .Where(t => t.Alias != null &&
                            string.Equals(t.Division, "fbs", StringComparison.OrdinalIgnoreCase))
                .Select(t => t.Alias!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var allFbsNames = fbsNames.Concat(aliasNames).ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Map to model — only include transfers involving at least one FBS team.
            var portalEntries = entries
                .Where(e => e.Eligibility != "Withdrawn" &&
                            (allFbsNames.Contains(e.Origin ?? "") ||
                             allFbsNames.Contains(e.Destination ?? "")))
                .Select(e => new PortalEntry
                {
                    Season = season,
                    FirstName = e.FirstName,
                    LastName = e.LastName,
                    Position = e.Position,
                    Origin = e.Origin,
                    Destination = e.Destination,
                    TransferDate = e.TransferDate,
                    Rating = e.Rating,
                    Stars = e.Stars,
                    Eligibility = e.Eligibility
                })
                .ToList();

            await _uow.Portal.UpsertSeasonAsync(season, portalEntries, token);
            await _uow.SaveChangesAsync(token);

            return portalEntries.Count;
        }

        /// <summary>
        /// Loads portal entries for every season from startSeason to current.
        /// Portal data is only reliable from 2021 onward.
        /// </summary>
        public async Task<int> LoadPortalBulkAsync(int startSeason, CancellationToken token = default)
        {
            var currentSeason = DateTime.Now.Year;
            var total = 0;

            for (var season = startSeason; season <= currentSeason; season++)
            {
                token.ThrowIfCancellationRequested();
                var count = await LoadPortalAsync(season, token);
                total += count;
                await Task.Delay(300, token); // rate limit
            }

            return total;
        }
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

        public async Task<int> BuildAvgScoreDifferentialsAsync(int startYear, CancellationToken token = default)
        {
            // Clear existing V2 data
            await _uow.Lookups.ClearAvgScoreDifferentialsAsync(token);

            // Historical played games
            var games = await _uow.Games
                .GetPlayedGamesSinceYearAsync(startYear, token);

            // FBS teams only
            var teams = await _uow.Teams.GetDictionaryByTeamIdAsync(token);

            // Differential bucket storage
            var buckets = new Dictionary<double, List<(double Margin, double Total)>>();

            foreach (var game in games)
            {
                if (!game.HomeId.HasValue || !game.AwayId.HasValue)
                    continue;

                if (!teams.TryGetValue(game.HomeId.Value, out var homeTeam) ||
                    !teams.TryGetValue(game.AwayId.Value, out var awayTeam))
                    continue;

                // FBS only for now
                if (!string.Equals(homeTeam.Division, "fbs", StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(awayTeam.Division, "fbs", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Pregame records through previous week
                var priorWeek = Math.Max(game.Week - 1, 0);

                var records = await _uow.WeeklyRankings.GetByTeamsAndYearAndWeekAsync(
                    new[] { homeTeam.TeamId, awayTeam.TeamId },
                    game.Year,
                    priorWeek,
                    token);

                if (!records.TryGetValue(homeTeam.TeamId, out var homeRecord) ||
                    !records.TryGetValue(awayTeam.TeamId, out var awayRecord))
                    continue;

                // Strengths
                // Strengths
                var homeGamesPlayed = homeRecord.Wins + homeRecord.Losses;
                var awayGamesPlayed = awayRecord.Wins + awayRecord.Losses;

                // Existing normalized rankings
                var homeWinPct = RatingCalculator.BucketWinPct(
                    homeRecord.Wins,
                    homeGamesPlayed);

                var awayWinPct = RatingCalculator.BucketWinPct(
                    awayRecord.Wins,
                    awayGamesPlayed);

                // Expanded superiority space
                var homeStrength = RatingCalculator.ExpandStrength(homeRecord.Ranking ?? 0m);
                var awayStrength = RatingCalculator.ExpandStrength(awayRecord.Ranking ?? 0m);

                // Differential
                var rawDifferential = homeStrength - awayStrength;

                // Collapse sparse tails into stable cohorts
                if (rawDifferential > 2.75m) 
                    rawDifferential = 3.0m;
                else if (rawDifferential > 2.5m) 
                    rawDifferential = 2.75m;
                else if (rawDifferential < -2.75m) 
                    rawDifferential = -3.0m;
                else if (rawDifferential < -2.5m) 
                    rawDifferential = -2.75m;

                var differential =
                    Math.Round(
                        (double)(rawDifferential / 0.05m),
                        MidpointRounding.AwayFromZero) * 0.05;

                // Observed values
                var margin = (double)((game.HomePoints ?? 0) - (game.AwayPoints ?? 0));
                var total = (double)((game.HomePoints ?? 0) + (game.AwayPoints ?? 0));

                // Ensure bucket exists
                if (!buckets.ContainsKey(differential))
                    buckets[differential] = new List<(double, double)>();

                if (!buckets.ContainsKey(-differential))
                    buckets[-differential] = new List<(double, double)>();

                // Store BOTH perspectives
                buckets[differential].Add((margin, total));
                buckets[-differential].Add((-margin, total));
            }

            // Aggregate results
            var differentials = new List<AvgScoreDifferential>();

            foreach (var bucket in buckets.OrderBy(b => b.Key))
            {
                var differential = bucket.Key;
                var samples = bucket.Value;

                if (samples.Count == 0)
                    continue;

                var margins = samples.Select(s => s.Margin).ToList();
                var totals = samples.Select(s => s.Total).ToList();

                var avgMargin = margins.Average();

                var variance = margins
                    .Select(m => Math.Pow(m - avgMargin, 2))
                    .Average();

                var stdDev = Math.Sqrt(variance);

                differentials.Add(new AvgScoreDifferential
                {
                    StrengthDifferential = (decimal)differential,
                    AverageMargin = (decimal)avgMargin,
                    StdDevMargin = (decimal)stdDev,
                    AverageTotalPoints = (decimal)totals.Average(),
                    SampleSize = samples.Count,
                    LastUpdatedUtc = DateTime.UtcNow
                });
            }

            await _uow.Lookups.AddAvgScoreDifferentialsAsync(
                differentials,
                token);

            await _uow.SaveChangesAsync(token);

            return differentials.Count();
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
        /// Assigns correct week numbers (17, 18, 19...) to postseason games for a given year.
        /// CFBD returns week=1 for all postseason games; this fixes that by bucketing on
        /// game date (Thursday-anchored weeks) and assigning sequential weeks from 17.
        /// Example: POST /api/developer/assignPostseasonWeeks?year=2024
        /// </summary>
        public async Task<int> AssignPostseasonWeeksAsync(int year, CancellationToken token = default)
        {
            var games = await _uow.Games.GetByYearAsync(year, token);
            var postseason = games.Where(g => g.SeasonType == "postseason"
                                           && g.GameDate != null
                                           && DateTime.TryParse(g.GameDate, out _)).ToList();

            if (postseason.Count == 0)
            {
                Console.WriteLine($"AssignPostseasonWeeksAsync: no postseason games found for {year}");
                return 0;
            }

            var regularGames = games.Where(g => g.SeasonType == "regular" && g.GameDate != null).ToList();

            var maxRegularWeek = regularGames.Max(g => g.Week);

            var regularWeekByTuesdayStart = regularGames
                .Where(g => DateTime.TryParse(g.GameDate, out _))
                .GroupBy(g =>
                {
                    DateTime.TryParse(g.GameDate, out var dt);
                    var daysFromTuesday = ((int)dt.DayOfWeek + 6) % 7;
                    return dt.Date.AddDays(-daysFromTuesday);
                })
                .ToDictionary(grp => grp.Key, grp => grp.First().Week);

            var weekMap = BuildPostseasonWeekMapFromGames(postseason, regularWeekByTuesdayStart, maxRegularWeek);
            foreach (var game in postseason)
                if (weekMap.TryGetValue(game.GameId, out var pw))
                    game.Week = pw;

            await _uow.SaveChangesAsync(token);
            Console.WriteLine($"AssignPostseasonWeeksAsync: assigned postseason weeks for {postseason.Count} games in {year}");
            return postseason.Count;
        }

        /// <summary>
        /// Bulk version — runs AssignPostseasonWeeksAsync for every year from startYear to current.
        /// Example: POST /api/developer/assignPostseasonWeeksBulk?startYear=1963
        /// </summary>
        public async Task<int> AssignPostseasonWeeksBulkAsync(int startYear, CancellationToken token = default)
        {
            var currentYear = DateTime.Now.Month < 8 ? DateTime.Now.Year - 1 : DateTime.Now.Year;
            var total = 0;

            for (var year = startYear; year <= currentYear; year++)
            {
                total += await AssignPostseasonWeeksAsync(year, token);
                await Task.Delay(50, token); // no HTTP calls, just DB — short delay is fine
            }

            Console.WriteLine($"AssignPostseasonWeeksBulkAsync: {total} total postseason games updated from {startYear} to {currentYear}");
            return total;
        }

        /// <summary>
        /// Buckets Games entities by Thursday-anchored calendar week, assigns week 17, 18, 19...
        /// </summary>
        private static Dictionary<int, int> BuildPostseasonWeekMapFromGames(
            List<Games> postseason,
            Dictionary<DateTime, int> regularWeekByTuesdayStart,
            int maxRegularWeek)
        {
            var parsed = postseason
                .Select(g =>
                {
                    DateTime.TryParse(g.GameDate, out var dt);
                    var daysFromTuesday = ((int)dt.DayOfWeek + 6) % 7; // Tue=0 ... Mon=6
                    var weekStart = dt.Date.AddDays(-daysFromTuesday);
                    return (g.GameId, weekStart);
                })
                .ToList();

            var distinctBuckets = parsed
                .Select(x => x.weekStart)
                .Distinct()
                .OrderBy(w => w)
                .ToList();

            var weekLabels = new Dictionary<DateTime, int>();
            var nextFallback = maxRegularWeek + 1;

            foreach (var bucket in distinctBuckets)
            {
                if (regularWeekByTuesdayStart.TryGetValue(bucket, out var existingWeek))
                    weekLabels[bucket] = existingWeek;
                else
                    weekLabels[bucket] = nextFallback++;
            }

            return parsed.ToDictionary(x => x.GameId, x => weekLabels[x.weekStart]);
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
                var weeks = (await _uow.Games.GetByYearAsync(year, token))
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

            await _uow.Teams.UpsertRangeAsync(teams, token);
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
            var url = $"/games?year={year}&seasonType=both&classification=fbs";
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

            await _uow.Games.UpsertRangeAsync(games, token);
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

        public async Task<int> SetSeasonTypeAsync(List<int> gameIds, string seasonType, CancellationToken token = default)
        {
            var games = await _uow.Games.GetByIds(gameIds, token);

            foreach (var game in games)
                game.SeasonType = seasonType;

            await _uow.SaveChangesAsync(token);
            return games.Count;
        }


        public async Task UpdateTeamRecordsAsync(int? targetYear = null, CancellationToken token = default)
        {
            try
            {
                await _uow.TeamRecords.UpsertFromGamesAsync(targetYear, token);
                await _uow.SaveChangesAsync(token);
            }
            catch (DbUpdateException ex) when (ex.InnerException is SqliteException sqliteEx)
            {
                // This gives you the specific SQLite error code (787 for Foreign Key)
                Console.WriteLine($"SQLite Error Code: {sqliteEx.SqliteErrorCode}");
                Console.WriteLine($"SQLite Extended Error Code: {sqliteEx.SqliteExtendedErrorCode}");
                Console.WriteLine($"Message: {sqliteEx.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating team records: {ex.Message}");
                if (ex.InnerException != null)
                    Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
                throw;
            }
        }
    }
}
