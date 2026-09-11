using System.Globalization;
using SaturdayPulse.Api.Contracts.Responses;
using SaturdayPulse.Contracts;
using SaturdayPulse.Models;

namespace SaturdayPulse.Services
{
    /// <summary>
    /// Polls CFBD for score updates every 5 minutes, but only while at
    /// least one of today's games is individually inside its own
    /// [KickoffTime, KickoffTime + 5 hours] window — not a single window
    /// spanning today's earliest-to-latest kickoff, which would keep
    /// polling through the dead gap between an early game ending and a
    /// late game starting. Outside any game's window (including any day
    /// with no games) this does nothing and makes no CFBD call.
    ///
    /// Scores only (HomePoints/AwayPoints) — Vegas odds are deliberately
    /// left alone here. The Season-Pass-gated manual single-game refresh
    /// (ProductionGameDataService.GetGameAsync) is the only path that
    /// touches odds on demand. No rating/ranking/rolling-average
    /// recalculation is triggered by this service.
    ///
    /// Switched 2026-09 to CFBD's /scoreboard?classification=fbs — one call
    /// per tick returns every game in CFBD's current window (not scoped by
    /// year/week the way /lines is), filtered locally to today's games.
    /// Confirmed against a real response spanning 2026-08-29 through
    /// 2026-09-07.
    /// </summary>
    public class GameScorePollingService(
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory httpClientFactory,
        ILogger<GameScorePollingService> logger,
        PollingStatusService status) : BackgroundService
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
                status.RecordTick();

                try
                {
                    await PollIfInWindowAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    // A bad tick should never take the whole background loop down —
                    // log and wait for the next PeriodicTimer tick.
                    logger.LogError(ex, "GameScorePollingService: unhandled error during poll tick");
                    status.RecordError(ex);
                }
            }
        }

        private async Task PollIfInWindowAsync(CancellationToken token)
        {
            // BackgroundService is a singleton; IUnitOfWork is scoped — new
            // scope per tick, same pattern as any other background-job-style
            // consumer of scoped services in ASP.NET Core.
            await using var scope = scopeFactory.CreateAsyncScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            var todayDate = DateTime.Now.Date;
            var today = todayDate.ToString("yyyy-MM-dd");
            var yesterday = todayDate.AddDays(-1).ToString("yyyy-MM-dd");
            var yearNow = DateTime.Now.Year;

            // GetByYearAsync already exists and is proven elsewhere in this
            // service layer; a season is small enough (a few hundred rows)
            // that filtering in-memory here beats adding a new by-date
            // repository method just for this.
            var weekGames = await uow.Games.GetGamesForCurrentWeekAsync(yearNow, token);

            // GetGamesForCurrentWeekAsync returns the WHOLE CFB week (can span
            // Tue–Mon), not just today — bracketing the poll window off every
            // game in that whole set (the previous version's bug) meant
            // windowStart/windowEnd spanned the entire week, so this ran
            // continuously for days at a time, not just across today's games.
            // Filtering to today first is what the `today` var above was
            // always meant to do but never actually did.
            //
            // Includes yesterday too — a late West Coast/Hawaii kickoff (e.g.
            // 10 PM Eastern) keeps GameDate stamped as the day it started,
            // but its [KickoffTime, KickoffTime + PostKickoffMargin] window
            // can extend past local midnight. The date filter here only
            // narrows the candidate set; anyGameInProgress below is what
            // actually decides "in progress," and it's fully time-aware, so
            // a stale yesterday's game correctly falls out once its own
            // window has passed — this isn't reintroducing the whole-week bug.
            var seasonGames = weekGames.Where(g => g.GameDate == today || g.GameDate == yesterday).ToList();

            if (seasonGames.Count == 0)
            {
                logger.LogDebug("GameScorePollingService: no games today or carrying over from yesterday ({Today}) — skipping.", today);
                status.RecordSkip("No games today");
                return;
            }

            var kickoffTimes = seasonGames              
                .Select(g => TryParseKickoffTime($"{g.GameDate} {g.KickoffTime}", out var kt) ? kt : (DateTime?)null)
                .Where(kt => kt.HasValue)
                .Select(kt => kt!.Value)
                .Distinct()
                .ToList();

            if (kickoffTimes.Count == 0)
            {
                // Rows from before the KickoffTime column existed, or a CFBD
                // StartDate that failed to parse. Can't safely determine a
                // window without it — skip rather than guess.
                logger.LogWarning(
                    "GameScorePollingService: {Count} game(s) today ({Today}) but none have KickoffTime set — skipping until re-loaded.",
                    seasonGames.Count, today);
                status.RecordSkip("Games today but no KickoffTime set");
                return;
            }

            var now = DateTime.Now;

            // In progress means NOW falls inside at least one individual
            // game's own [kickoff, kickoff + PostKickoffMargin] window — NOT
            // today's earliest-kickoff-to-latest-kickoff span, which is what
            // was polling straight through the dead gap between, say, a noon
            // game ending and a primetime game starting.
            var anyGameInProgress = kickoffTimes.Any(kt => now >= kt && now <= kt + PostKickoffMargin);

            if (!anyGameInProgress)
            {
                logger.LogDebug(
                    "GameScorePollingService: no game today currently in its kickoff window (now={Now}) — skipping.",
                    now);
                status.RecordSkip("No game currently in its kickoff window");
                return;
            }

            var todaysGameIds = seasonGames.Select(g => g.GameId).ToHashSet();
            var updatedCount = 0;

            // /scoreboard returns every game currently in CFBD's window in one
            // call — unlike /lines, it isn't scoped by year+week, so there's no
            // per-combo loop needed anymore. Filtered locally to just today's
            // games, same pattern the old /lines-based approach used.
            // classification=fbs matches this app's scope (no FCS/other
            // divisions tracked elsewhere).
            var response = await CfbdClient.GetAsync("/scoreboard?classification=fbs", token);
            status.RecordCfbdCall(response.IsSuccessStatusCode);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "GameScorePollingService: CFBD /scoreboard returned {StatusCode} — skipping this tick.",
                    response.StatusCode);
                return;
            }

            var scoreboardGames = await response.Content
                .ReadFromJsonAsync<List<CfbdScoreboardGameDto>>(cancellationToken: token) ?? [];

            foreach (var dto in scoreboardGames)
            {
                if (!todaysGameIds.Contains(dto.Id)) continue;

                // Skip games CFBD hasn't started tracking a score for yet
                // (points is null pre-kickoff in this endpoint) rather than
                // writing a null over whatever's already in Games — a
                // deliberate change from the old /lines-based version, which
                // wrote HomeScore/AwayScore unconditionally.
                if (dto.HomeTeam?.Points == null || dto.AwayTeam?.Points == null) continue;

                var game = seasonGames.First(g => g.GameId == dto.Id);
                game.HomePoints = dto.HomeTeam.Points;
                game.AwayPoints = dto.AwayTeam.Points;

                // Status/Period/Clock (2026-09-05) — written unconditionally
                // alongside points, unlike the points null-guard above: once
                // a game has a score being tracked, status/period/clock are
                // expected to be present too, and skipping just leaves a
                // stale clock in the row from the previous tick.
                // REQUIRES: Games.Status (string?), Games.Period (int?),
                // Games.Clock (string?) columns — not yet added, see seed.
                game.Status = dto.Status;
                game.Period = dto.Period;
                game.Clock = dto.Clock;

                await uow.Games.UpsertAsync(game, token);
                updatedCount++;
            }

            if (updatedCount > 0)
            {
                await uow.SaveChangesAsync(token);
                logger.LogInformation(
                    "GameScorePollingService: refreshed {Count} of {Total} game(s) for {Today}.",
                    updatedCount, seasonGames.Count, today);
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
