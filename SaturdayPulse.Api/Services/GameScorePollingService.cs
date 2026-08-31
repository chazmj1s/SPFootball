using System.Globalization;
using SaturdayPulse.Contracts;
using SaturdayPulse.Models;

namespace SaturdayPulse.Services
{
    /// <summary>
    /// Polls CFBD for score updates every 5 minutes, but only while at
    /// least one of today's games is plausibly in progress — the window is
    /// [earliest KickoffTime today, latest KickoffTime today + 5 hours].
    /// Outside that window (including any day with no games) this does
    /// nothing and makes no CFBD call.
    ///
    /// Scores only (HomePoints/AwayPoints) — Vegas odds are deliberately
    /// left alone here. The Season-Pass-gated manual single-game refresh
    /// (ProductionGameDataService.GetGameAsync) is the only path that
    /// touches odds on demand. No rating/ranking/rolling-average
    /// recalculation is triggered by this service.
    /// </summary>
    public class GameScorePollingService(
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory httpClientFactory,
        ILogger<GameScorePollingService> logger) : BackgroundService
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan PostKickoffMargin = TimeSpan.FromHours(5);

        // Must stay identical to GameDataService.LoadGamesAsync's
        // KickoffTimeFormat const — that's the only other place this
        // column gets written.
        private const string KickoffTimeFormat = "yyyy-MM-dd HH:mm:ss";

        // Same "cfbd" named client GameDataService/ProductionGameDataService use.
        private HttpClient CfbdClient => httpClientFactory.CreateClient("cfbd");

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(PollInterval);

            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await PollIfInWindowAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    // A bad tick should never take the whole background loop down —
                    // log and wait for the next PeriodicTimer tick.
                    logger.LogError(ex, "GameScorePollingService: unhandled error during poll tick");
                }
            }
        }

        private async Task PollIfInWindowAsync(CancellationToken token)
        {
            // BackgroundService is a singleton; IUnitOfWork is scoped — new
            // scope per tick, same pattern as any other background-job-style
            // consumer of scoped services in ASP.NET Core.
            using var scope = scopeFactory.CreateScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            var today = DateTime.Now.ToString("MM/dd/yyyy");
            var yearNow = DateTime.Now.Year;

            // GetByYearAsync already exists and is proven elsewhere in this
            // service layer; a season is small enough (a few hundred rows)
            // that filtering in-memory here beats adding a new by-date
            // repository method just for this.
            var seasonGames = await uow.Games.GetByYearAsync(yearNow, token);
            var todaysGames = seasonGames.Where(g => g.GameDate == today).ToList();

            if (todaysGames.Count == 0)
            {
                logger.LogDebug("GameScorePollingService: no games today ({Today}) — skipping.", today);
                return;
            }

            var kickoffTimes = todaysGames
                .Select(g => TryParseKickoffTime(g.KickoffTime, out var kt) ? kt : (DateTime?)null)
                .Where(kt => kt.HasValue)
                .Select(kt => kt!.Value)
                .ToList();

            if (kickoffTimes.Count == 0)
            {
                // Rows from before the KickoffTime column existed, or a CFBD
                // StartDate that failed to parse. Can't safely determine a
                // window without it — skip rather than guess.
                logger.LogWarning(
                    "GameScorePollingService: {Count} game(s) today ({Today}) but none have KickoffTime set — skipping until re-loaded.",
                    todaysGames.Count, today);
                return;
            }

            var windowStart = kickoffTimes.Min();
            var windowEnd = kickoffTimes.Max() + PostKickoffMargin;
            var now = DateTime.Now;

            if (now < windowStart || now > windowEnd)
            {
                logger.LogDebug(
                    "GameScorePollingService: outside today's window ({Start}–{End}), now={Now} — skipping.",
                    windowStart, windowEnd, now);
                return;
            }

            var todaysGameIds = todaysGames.Select(g => g.GameId).ToHashSet();
            var updatedCount = 0;

            // One CFBD call per distinct (Year, Week) combo present today —
            // same bulk /lines endpoint GameDataService.LoadLinesAsync uses.
            // Almost always exactly one combo; the loop just covers the rare
            // season-boundary case where two combos land on the same date.
            foreach (var (year, week) in todaysGames.Select(g => (g.Year, g.Week)).Distinct())
            {
                var response = await CfbdClient.GetAsync($"/lines?year={year}&week={week}&seasonType=both", token);
                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning(
                        "GameScorePollingService: CFBD returned {StatusCode} for year={Year} week={Week} — skipping this combo.",
                        response.StatusCode, year, week);
                    continue;
                }

                var lineDtos = await response.Content
                    .ReadFromJsonAsync<List<CfbdLinesGameDto>>(cancellationToken: token) ?? [];

                foreach (var dto in lineDtos)
                {
                    if (!todaysGameIds.Contains(dto.Id)) continue;

                    var game = todaysGames.First(g => g.GameId == dto.Id);
                    game.HomePoints = dto.HomeScore;
                    game.AwayPoints = dto.AwayScore;
                    await uow.Games.UpsertAsync(game, token);
                    updatedCount++;
                }
            }

            if (updatedCount > 0)
            {
                await uow.SaveChangesAsync(token);
                logger.LogInformation(
                    "GameScorePollingService: refreshed {Count} of {Total} game(s) for {Today}.",
                    updatedCount, todaysGames.Count, today);
            }
        }

        /// <summary>
        /// Parses a KickoffTime column value against the exact, fixed,
        /// culture-invariant format LoadGamesAsync writes it in. Deliberately
        /// NOT DateTime.TryParse — that's locale-dependent and this needs to
        /// round-trip identically regardless of server culture settings.
        /// </summary>
        private static bool TryParseKickoffTime(string? value, out DateTime result) =>
            DateTime.TryParseExact(
                value, KickoffTimeFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out result);
    }
}
