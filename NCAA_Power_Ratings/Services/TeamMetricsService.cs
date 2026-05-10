using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NCAA_Power_Ratings.Configuration;
using NCAA_Power_Ratings.Data;
using NCAA_Power_Ratings.Interfaces;
using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NCAA_Power_Ratings.Services
{
    /// <summary>
    /// Service for calculating team performance metrics and trends.
    /// </summary>
    public class TeamMetricsService
    {
        private readonly IDbContextFactory<NCAAContext> _contextFactory;
        private readonly MetricsConfiguration _config;

        public TeamMetricsService(
            IDbContextFactory<NCAAContext> contextFactory,
            IOptions<MetricsConfiguration> config)
        {
            _contextFactory = contextFactory;
            _config = config.Value;
        }

        /// <summary>
        /// Calculates projected wins for all teams based on 10-year historical data.
        /// Uses weighted average (recent years weighted more) and normalizes for season length.
        /// </summary>
        private async Task<Dictionary<int, int>> CalculateProjectedWins(NCAAContext context, int endYear, CancellationToken token = default)
        {
            var standardSeasonGames = _config.StandardSeasonGames;
            var extraWinBump = _config.ExtraWinBump;
            var startYear = endYear - _config.ProjectedWinsHistoryYears;

            // Get team records from last 10 years (excluding endYear)
            var teamRecords = await context.TeamRecords
                .Where(tr => tr.Year >= startYear && tr.Year < endYear)
                .ToListAsync(token);

            // Group by team and calculate projected wins
            var projectedWins = teamRecords
                .GroupBy(tr => tr.TeamID)
                .Select(g =>
                {
                    // Order by year ascending (oldest to newest), take last 10
                    var records = g.OrderBy(r => r.Year).TakeLast(10).ToList();

                    // Calculate normalized win percentage for each year
                    var normalizedPercentages = records.Select(r =>
                    {
                        standardSeasonGames = r.RegularSeasonGames;

                        int totalGames = r.Wins + r.Losses;
                        if (totalGames <= 0)
                            return 0.0;

                        double normalized;
                        if (totalGames <= standardSeasonGames)
                        {
                            normalized = (double)r.Wins / totalGames;
                        }
                        else
                        {
                            int baseWins = Math.Min(r.Wins, standardSeasonGames);
                            int extraWins = Math.Max(0, r.Wins - standardSeasonGames);
                            normalized = (baseWins + (extraWins * extraWinBump)) / standardSeasonGames;
                        }

                        return normalized;
                    }).ToList();

                    // Calculate weighted average (most recent year = highest weight)
                    double weightedAverage = 0.0;
                    if (normalizedPercentages.Count > 0)
                    {
                        int n = normalizedPercentages.Count;
                        long weightSum = (long)n * (n + 1) / 2;

                        weightedAverage = normalizedPercentages
                            .Select((pct, index) => pct * (index + 1))
                            .Sum() / weightSum;
                    }

                    // Calculate projected wins
                    double projectedWinsDecimal = weightedAverage * standardSeasonGames;
                    int projectedWinsValue = projectedWinsDecimal - Math.Floor(projectedWinsDecimal) >= _config.ProjectedWinsRoundingThreshold
                        ? (int)Math.Ceiling(projectedWinsDecimal)
                        : (int)Math.Floor(projectedWinsDecimal);

                    return new { TeamID = g.Key, ProjectedWins = projectedWinsValue };
                })
                .ToDictionary(x => x.TeamID, x => x.ProjectedWins);

            return projectedWins;
        }

        /// <summary>
        /// Calculates team performance trends based on the last 10 years of data.
        /// Returns normalized win percentages (oldest to newest) plus weighted average and projected wins.
        /// Normalizes for 12-game seasons: <=12 games uses actual percentage, >12 games gets 0.25 bump per extra win.
        /// Most recent year receives the highest weight in the weighted average calculation.
        /// Projected wins = weighted average × 12, rounded to nearest integer using 0.75 as boundary.
        /// </summary>
        public async Task<string> CalculateTrend(CancellationToken token = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(token);

            try
            {
                var standardSeasonGames = 12; //default to current standard; actual season length can vary by year, but we normalize to 12 for projection
                const double extraWinBump = 0.25;

                var currentYear = DateTime.Now.Year;
                var startYear = currentYear - 10;

                // Get team records from last 10 years
                var teamRecords = await context.TeamRecords
                    .Where(tr => tr.Year >= startYear)
                    .Include(tr => tr.Team)
                    .ToListAsync(token);

                // Group by team and calculate normalized win percentages with weighted average
                var results = teamRecords
                    .GroupBy(tr => tr.TeamID)
                    .Select(g =>
                    {
                        // Order by year ascending (oldest to newest), take last 10
                        var records = g.OrderBy(r => r.Year).TakeLast(10).ToList();

                        // Calculate normalized win percentage for each year (oldest to newest)
                        var normalizedPercentages = records.Select(r =>
                        {
                            standardSeasonGames = r.RegularSeasonGames; // Get standard season games for the year

                            int totalGames = r.Wins + r.Losses;
                            if (totalGames <= 0)
                                return 0.0;

                            double normalized;
                            if (totalGames <= standardSeasonGames)
                            {
                                // Use actual win percentage
                                normalized = (double)r.Wins / totalGames;
                            }
                            else
                            {
                                // 12-game denominator with 0.25 bump for each extra win
                                int baseWins = Math.Min(r.Wins, standardSeasonGames);
                                int extraWins = Math.Max(0, r.Wins - standardSeasonGames);
                                normalized = (baseWins + (extraWins * extraWinBump)) / standardSeasonGames;
                            }

                            return Math.Round(normalized, 4);
                        }).ToList();

                        // Calculate weighted average (most recent year = highest weight)
                        double weightedAverage = 0.0;
                        if (normalizedPercentages.Count > 0)
                        {
                            int n = normalizedPercentages.Count;
                            long weightSum = (long)n * (n + 1) / 2;

                            // Reverse iterate to give highest weight to most recent (last in array)
                            weightedAverage = normalizedPercentages
                                .Select((pct, index) => pct * (index + 1)) // index 0 = oldest = weight 1, last = weight n
                                .Sum() / weightSum;
                        }

                        // Calculate projected wins (weighted average × 12)
                        double projectedWinsDecimal = weightedAverage * standardSeasonGames;

                        // Round using 0.75 as boundary: if fractional part >= 0.75, round up; otherwise round down
                        int projectedWins = projectedWinsDecimal - Math.Floor(projectedWinsDecimal) >= 0.75
                            ? (int)Math.Ceiling(projectedWinsDecimal)
                            : (int)Math.Floor(projectedWinsDecimal);

                        return new
                        {
                            TeamID = g.Key,
                            TeamName = g.First().Team?.TeamName ?? "Unknown",
                            Years = records.Select(r => r.Year).ToList(), // Oldest to newest
                            NormalizedWinPercentages = normalizedPercentages, // Oldest to newest
                            WeightedAverage = Math.Round(weightedAverage, 4),
                            ProjectedWins = projectedWins,
                            RecordCount = records.Count
                        };
                    })
                    .OrderByDescending(r => r.ProjectedWins).OrderByDescending(r => r.WeightedAverage)
                    .ToList();

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };

                return JsonSerializer.Serialize(results, options);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error calculating team trends: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Calculates and sets Strength of Schedule (SOS) values for all teams in the specified year.
        /// Computes BaseSOS (first-order), SubSOS (second-order), and CombinedSOS (weighted blend).
        /// Week 0 (default): initializes year with 10-year projected wins.
        /// Week 1-5: no-op (too early for meaningful SOS).
        /// Week 6+: uses current year wins.
        /// </summary>
        /// <param name="year">The year to calculate SOS for. Defaults to current year.</param>
        /// <param name="week">The current week. Defaults to 0 (initialize with projected wins). 1-5 = no-op, 6+ = use current year wins.</param>
        /// <param name="token">Cancellation token.</param>
        public async Task SetSOS(int? year = null, int? week = null, CancellationToken token = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(token);

            try
            {
                var targetYear = year ?? DateTime.Now.Year;
                var targetWeek = week ?? 0;

                // Step 1: Determine which win data to use
                // If week 1-5, no-op (too early for meaningful SOS)
                Dictionary<int, int> winsLookup;
                Dictionary<int, int> lossesLookup;

                if (targetWeek == 0)
                {
                    // Read pre-computed SeedRating from TeamRecords.
                    // RollingAverageService must run before SetSOS in the pipeline.
                    // SeedRating is a win-percentage (0.0–1.0+); multiply by standard season
                    // games to get a projected win count compatible with the SOS weight logic.
                    var seedRecords = await context.TeamRecords
                        .Where(tr => tr.Year == targetYear && tr.SeedRating != null)
                        .ToListAsync(token);

                    winsLookup = seedRecords.ToDictionary(
                        tr => tr.TeamID,
                        tr => (int)Math.Round((double)tr.SeedRating!.Value * tr.RegularSeasonGames));

                    lossesLookup = seedRecords.ToDictionary(
                        tr => tr.TeamID,
                        tr => tr.RegularSeasonGames - (int)Math.Round((double)tr.SeedRating!.Value * tr.RegularSeasonGames));
                }
                else
                {
                    if (targetWeek < _config.SosWeekThreshold)
                        return;
                    else
                    {
                        // Week threshold or later - use current year wins and losses
                        var teamRecords = await context.TeamRecords
                            .Where(tr => tr.Year == targetYear)
                            .ToDictionaryAsync(tr => tr.TeamID, token);

                        winsLookup = teamRecords.ToDictionary(kvp => kvp.Key, kvp => (int)kvp.Value.Wins);
                        lossesLookup = teamRecords.ToDictionary(kvp => kvp.Key, kvp => (int)kvp.Value.Losses);
                    }

                }

                // Step 2: GameParticipants - Union of games from winner and loser perspectives
                // Include team divisions for cross-divisional penalty
                var gamesFromWinner = from g in context.Game
                    where g.Year == targetYear
                    join t in context.Team on g.WinnerId equals t.TeamID
                    join opp in context.Team on g.LoserId equals opp.TeamID
                    select new
                    {
                        g.Year,
                        TeamId = g.WinnerId,
                        TeamName = g.WinnerName,
                        TeamDivision = t.Division,
                        OpponentId = g.LoserId,
                        OpponentName = g.LoserName,
                        OpponentDivision = opp.Division,
                        TeamPoints = g.WPoints,
                        OpponentPoints = g.LPoints,
                        g.Location,
                        IsHomeTeam = g.Location == 'W'
                    };

                var gamesFromLoser = from g in context.Game
                    where g.Year == targetYear
                    join t in context.Team on g.LoserId equals t.TeamID
                    join opp in context.Team on g.WinnerId equals opp.TeamID
                    select new
                    {
                        g.Year,
                        TeamId = g.LoserId,
                        TeamName = g.LoserName,
                        TeamDivision = t.Division,
                        OpponentId = g.WinnerId,
                        OpponentName = g.WinnerName,
                        OpponentDivision = opp.Division,
                        TeamPoints = g.LPoints,
                        OpponentPoints = g.WPoints,
                        g.Location,
                        IsHomeTeam = g.Location == 'L'
                    };

                var gameParticipants = await gamesFromWinner
                    .Union(gamesFromLoser)
                    .ToListAsync(token);

                // Step 3: Assign wins and losses to each game participant
                var withRecords = gameParticipants
                    .Select(gp => new
                    {
                        gp.Year,
                        gp.TeamId,
                        gp.TeamName,
                        gp.TeamDivision,
                        TeamWins = winsLookup.GetValueOrDefault(gp.TeamId, 0),
                        TeamLosses = lossesLookup.GetValueOrDefault(gp.TeamId, 0),
                        gp.OpponentId,
                        gp.OpponentName,
                        gp.OpponentDivision,
                        gp.TeamPoints,
                        gp.OpponentPoints,
                        OpponentWins = winsLookup.GetValueOrDefault(gp.OpponentId, 0),
                        OpponentLosses = lossesLookup.GetValueOrDefault(gp.OpponentId, 0),
                        gp.Location,
                        gp.IsHomeTeam
                    })
                    .ToList();

                // Step 4: Join with AvgScoreDeltas and MatchupHistory for rivalry adjustments
                var homeFieldAdvantage = _config.HomeFieldAdvantage;
                var avgScoreDeltas = await context.AvgScoreDeltas.ToListAsync(token);
                var matchupHistories = await context.MatchupHistories.ToListAsync(token);

                var withDeltas = withRecords
                    .Select(r =>
                    {
                        var delta = r.TeamPoints - r.OpponentPoints;

                        // Calculate win percentages (round to 0.05 increments for 5% buckets)
                        var teamGamesPlayed = r.TeamWins + r.TeamLosses;
                        var oppGamesPlayed = r.OpponentWins + r.OpponentLosses;

                        var teamWinPct = teamGamesPlayed > 0 
                            ? Math.Round((decimal)r.TeamWins / teamGamesPlayed * 20m, MidpointRounding.AwayFromZero) / 20m
                            : 0m;
                        var oppWinPct = oppGamesPlayed > 0 
                            ? Math.Round((decimal)r.OpponentWins / oppGamesPlayed * 20m, MidpointRounding.AwayFromZero) / 20m
                            : 0m;

                        var maxWinPct = Math.Max(teamWinPct, oppWinPct);
                        var minWinPct = Math.Min(teamWinPct, oppWinPct);

                        var asd = avgScoreDeltas.FirstOrDefault(a =>
                            a.Team1WinPct == maxWinPct && a.Team2WinPct == minWinPct);

                        double zValue = 0.0;
                        if (asd != null && asd.StDevP != 0)
                        {
                            // Expected delta is from higher-win team's perspective
                            var expectedDelta = (double)asd.AverageScoreDelta;

                            // Adjust to team's perspective
                            var expectedFromTeamPerspective = teamWinPct >= oppWinPct
                                ? expectedDelta
                                : -expectedDelta;

                            // Adjust for home field advantage
                            if (r.IsHomeTeam)
                                expectedFromTeamPerspective += homeFieldAdvantage;
                            else if (r.Location != 'N') // opponent is home
                                expectedFromTeamPerspective -= homeFieldAdvantage;
                            // else: neutral site, no adjustment

                            // Check for rivalry matchup and apply variance multiplier
                            var normalizedTeam1 = Math.Min(r.TeamId, r.OpponentId);
                            var normalizedTeam2 = Math.Max(r.TeamId, r.OpponentId);

                            var matchupHistory = matchupHistories.FirstOrDefault(m =>
                                m.Team1Id == normalizedTeam1 && m.Team2Id == normalizedTeam2);

                            var effectiveStDev = (double)asd.StDevP;

                            if (matchupHistory != null)
                            {
                                // Get tier-based variance multiplier
                                var tierMultiplier = matchupHistory.RivalryTier switch
                                {
                                    "EPIC" => 1.75,
                                    "NATIONAL" => 1.5,
                                    "STATE" => 1.3,
                                    "MEH" => 1.1,
                                    _ => 1.0
                                };

                                // Apply the multiplier to increase variance for rivalry games
                                effectiveStDev *= tierMultiplier;
                            }

                            zValue = (delta - expectedFromTeamPerspective) / effectiveStDev;
                        }

                        return new
                        {
                            r.Year,
                            r.TeamId,
                            r.TeamName,
                            r.TeamDivision,
                            r.TeamWins,
                            r.OpponentId,
                            r.OpponentName,
                            r.OpponentDivision,
                            r.TeamPoints,
                            r.OpponentPoints,
                            Delta = delta,
                            ZValue = zValue
                        };
                    })
                    .ToList();

                // Step 5: Assign weights based on Z-values and division
                var withWeights = withDeltas
                    .Select(d => new
                    {
                        d.Year,
                        d.TeamId,
                        d.TeamName,
                        d.OpponentId,
                        d.OpponentName,
                        d.OpponentDivision,
                        Weight = d.ZValue switch
                        {
                            >= 1.0 => 1.25,
                            > -1.0 => 1.00,
                            > -2.0 => 0.75,
                            _ => 0.50
                        },
                        // Division weight: FCS opponents count as 0.25, FBS as 1.0
                        DivisionWeight = (d.OpponentDivision == "FCS") ? 0.25 : 1.0
                    })
                    .ToList();

                // Step 6: Calculate BaseSOS per team with division weighting
                var baseSOS = withWeights
                    .GroupBy(w => new { w.Year, w.TeamId })
                    .Select(g => new
                    {
                        g.Key.Year,
                        g.Key.TeamId,
                        // Weighted by both game performance AND opponent division
                        BaseSOS = Math.Round(
                            g.Sum(x => x.Weight * x.DivisionWeight) / g.Sum(x => x.DivisionWeight), 
                            3),
                        GamesPlayed = g.Count()
                    })
                    .ToList();

                // Step 7: Calculate OpponentSOS (join weights with opponents' BaseSOS)
                var opponentSOS = withWeights
                    .Join(
                        baseSOS,
                        w => new { w.Year, TeamId = w.OpponentId },
                        b => new { Year = b.Year, b.TeamId },
                        (w, b) => new
                        {
                            w.Year,
                            w.TeamId,
                            OppBaseSOS = b.BaseSOS,
                            w.Weight
                        }
                    )
                    .ToList();

                // Step 8: Calculate SecondOrderSOS (SubSOS)
                var secondOrderSOS = opponentSOS
                    .GroupBy(o => new { o.Year, o.TeamId })
                    .Select(g => new
                    {
                        g.Key.Year,
                        g.Key.TeamId,
                        SubSOS = Math.Round(
                            g.Sum(x => x.OppBaseSOS * x.Weight) / g.Sum(x => x.Weight),
                            3
                        )
                    })
                    .ToList();

                // Step 9: Combine BaseSOS and SubSOS
                var combined = baseSOS
                    .GroupJoin(
                        secondOrderSOS,
                        b => new { b.Year, b.TeamId },
                        s => new { s.Year, s.TeamId },
                        (b, s) => new
                        {
                            b.Year,
                            b.TeamId,
                            b.BaseSOS,
                            SubSOS = s.FirstOrDefault()?.SubSOS ?? b.BaseSOS,
                        }
                    )
                    .Select(c => new
                    {
                        c.Year,
                        c.TeamId,
                        c.BaseSOS,
                        c.SubSOS,
                        // Weighted 40% BaseSOS (direct opponents) + 60% SubSOS (opponents' schedules)
                        // Emphasizes sustained schedule difficulty over isolated tough games
                        CombinedSOS = Math.Round((2 * c.BaseSOS + 3 * c.SubSOS) / 5, 4)
                    })
                    .ToList();

                // Step 10: Update TeamRecords
                var teamRecordsToUpdate = await context.TeamRecords
                    .Where(tr => tr.Year == targetYear)
                    .Include(tr => tr.Team)
                    .ToListAsync(token);

                foreach (var record in teamRecordsToUpdate)
                {
                    var sosData = combined.FirstOrDefault(c =>
                        c.TeamId == record.TeamID && c.Year == record.Year);

                    if (record.Team.Division == "FCS")
                    {
                        // FCS teams get NULL for SOS values
                        record.BaseSOS = null;
                        record.SubSOS = null;
                        record.CombinedSOS = null;
                    }
                    else if (sosData != null)
                    {
                        record.BaseSOS = (decimal)sosData.BaseSOS;
                        record.SubSOS = (decimal)sosData.SubSOS;
                        record.CombinedSOS = (decimal)sosData.CombinedSOS;
                    }
                }

                await context.SaveChangesAsync(token);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error calculating SOS: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Calculates Power Rating for all teams in the specified year.
        /// PowerRating = AverageZScore × CombinedSOS
        /// AverageZScore = mean of per-game Z-scores for the season.
        /// </summary>
        public async Task CalculatePowerRatings(int? year = null, CancellationToken token = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(token);

            var targetYear = year ?? DateTime.Now.Year;

            // Step 1: Get all games for the year with team divisions
            var gamesFromWinner = from g in context.Game
                where g.Year == targetYear
                join t in context.Team on g.WinnerId equals t.TeamID
                join opp in context.Team on g.LoserId equals opp.TeamID
                select new {
                    g.Year,
                    TeamId = g.WinnerId,
                    TeamName = g.WinnerName,
                    TeamDivision = t.Division,
                    OpponentId = g.LoserId,
                    OpponentDivision = opp.Division,
                    TeamPoints = g.WPoints,
                    OpponentPoints = g.LPoints,
                    g.Location,
                    IsHomeTeam = g.Location == 'W'
                };

            var gamesFromLoser = from g in context.Game
                where g.Year == targetYear
                join t in context.Team on g.LoserId equals t.TeamID
                join opp in context.Team on g.WinnerId equals opp.TeamID
                select new {
                    g.Year,
                    TeamId = g.LoserId,
                    TeamName = g.LoserName,
                    TeamDivision = t.Division,
                    OpponentId = g.WinnerId,
                    OpponentDivision = opp.Division,
                    TeamPoints = g.LPoints,
                    OpponentPoints = g.WPoints,
                    g.Location,
                    IsHomeTeam = g.Location == 'L'
                };

            var gameParticipants = await gamesFromWinner
                .Union(gamesFromLoser)
                .ToListAsync(token);

            // Step 2: Get wins and losses lookup — use projected wins preseason, actual wins week 6+
            var teamRecords = await context.TeamRecords
                .Where(tr => tr.Year == targetYear)
                .ToDictionaryAsync(tr => tr.TeamID, token);

            var winsLookup = teamRecords.ToDictionary(kvp => kvp.Key, kvp => (int)kvp.Value.Wins);
            var lossesLookup = teamRecords.ToDictionary(kvp => kvp.Key, kvp => (int)kvp.Value.Losses);

            // Step 3: Load AvgScoreDeltas and MatchupHistory for rivalry adjustments
            var avgScoreDeltas = await context.AvgScoreDeltas.ToListAsync(token);
            var matchupHistories = await context.MatchupHistories.ToListAsync(token);

            // Step 4: Calculate Z-score per game per team with rivalry variance adjustments
            var homeFieldAdvantage = _config.HomeFieldAdvantage;

            var zScores = gameParticipants
                .Select(gp => {
                    var teamWins = winsLookup.GetValueOrDefault(gp.TeamId, 0);
                    var teamLosses = lossesLookup.GetValueOrDefault(gp.TeamId, 0);
                    var oppWins = winsLookup.GetValueOrDefault(gp.OpponentId, 0);
                    var oppLosses = lossesLookup.GetValueOrDefault(gp.OpponentId, 0);

                    // Calculate win percentages (round to 0.05 increments for 5% buckets)
                    var teamGamesPlayed = teamWins + teamLosses;
                    var oppGamesPlayed = oppWins + oppLosses;

                    var teamWinPct = teamGamesPlayed > 0 
                        ? Math.Round((decimal)teamWins / teamGamesPlayed * 20m, MidpointRounding.AwayFromZero) / 20m
                        : 0m;
                    var oppWinPct = oppGamesPlayed > 0 
                        ? Math.Round((decimal)oppWins / oppGamesPlayed * 20m, MidpointRounding.AwayFromZero) / 20m
                        : 0m;

                    var maxWinPct = Math.Max(teamWinPct, oppWinPct);
                    var minWinPct = Math.Min(teamWinPct, oppWinPct);

                    var delta = gp.TeamPoints - gp.OpponentPoints;

                    var asd = avgScoreDeltas.FirstOrDefault(
                        a => a.Team1WinPct == maxWinPct && a.Team2WinPct == minWinPct);

                    double zScore = 0.0;
                    if (asd != null && asd.StDevP != 0)
                    {
                        // Expected delta is always from higher-win team's perspective
                        var expectedDelta = (double)asd.AverageScoreDelta;

                        // Adjust expected to team's perspective:
                        // - If team is favored (higher win%): expect positive delta
                        // - If team is underdog (lower win%): expect negative delta
                        var expectedFromTeamPerspective = teamWinPct >= oppWinPct 
                            ? expectedDelta 
                            : -expectedDelta;

                        // Adjust for home field advantage
                        if (gp.IsHomeTeam)
                            expectedFromTeamPerspective += homeFieldAdvantage;
                        else if (gp.Location != 'N') // opponent is home
                            expectedFromTeamPerspective -= homeFieldAdvantage;
                        // else: neutral site, no adjustment

                        // Check for rivalry matchup and apply variance multiplier
                        var normalizedTeam1 = Math.Min(gp.TeamId, gp.OpponentId);
                        var normalizedTeam2 = Math.Max(gp.TeamId, gp.OpponentId);

                        var matchupHistory = matchupHistories.FirstOrDefault(m =>
                            m.Team1Id == normalizedTeam1 && m.Team2Id == normalizedTeam2);

                        var effectiveStDev = (double)asd.StDevP;

                        if (matchupHistory != null)
                        {
                            // Get tier-based variance multiplier
                            var tierMultiplier = matchupHistory.RivalryTier switch
                            {
                                "EPIC" => 1.75,
                                "NATIONAL" => 1.5,
                                "STATE" => 1.3,
                                "MEH" => 1.1,
                                _ => 1.0
                            };

                            // Apply the multiplier to increase variance for rivalry games
                            // Higher variance = lower Z-score magnitude = less impact on ratings
                            effectiveStDev *= tierMultiplier;
                        }

                        // Z-score: how much better/worse than expected
                        zScore = (delta - expectedFromTeamPerspective) / effectiveStDev;

                        // Apply logarithmic dampening to compress extreme Z-scores
                        // This reflects diminishing returns: a 20-point upset is impressive,
                        // but a 40-point upset isn't twice as impressive
                        if (zScore != 0)
                        {
                            var sign = Math.Sign(zScore);
                            var magnitude = Math.Abs(zScore);
                            zScore = sign * Math.Log(1 + magnitude);
                        }
                    }

                    // Division weight: FCS opponents count 0.25, FBS count 1.0
                    var divisionWeight = (gp.OpponentDivision == "FCS") ? 0.25 : 1.0;

                    return new { gp.TeamId, gp.TeamDivision, ZScore = zScore, DivisionWeight = divisionWeight };
                })
                .GroupBy(x => x.TeamId)
                .Select(g => new {
                    TeamId = g.Key,
                    TeamDivision = g.First().TeamDivision,
                    // Weighted average: FCS games count 0.25, FBS games count 1.0
                    AvgZScore = g.Sum(x => x.ZScore * x.DivisionWeight) / g.Sum(x => x.DivisionWeight)
                })
                .ToList();

            // Step 5: Update PowerRating for all teams
            var teamRecordsForUpdate = await context.TeamRecords
                .Where(tr => tr.Year == targetYear)
                .Include(tr => tr.Team)
                .ToListAsync(token);

            foreach (var record in teamRecordsForUpdate)
            {
                if (record.Team.Division == "FCS")
                {
                    // FCS teams get NULL for PowerRating
                    record.PowerRating = null;
                }
                else
                {
                    // FBS teams get calculated PowerRating
                    var zData = zScores.FirstOrDefault(z => z.TeamId == record.TeamID);
                    if (zData != null)
                    {
                        var sos = (double)(record.CombinedSOS ?? 1.0m);
                        record.PowerRating = (decimal)Math.Round(zData.AvgZScore * sos, 4);
                    }
                }
            }

            await context.SaveChangesAsync(token);
        }

        public async Task CalculateRankings(int targetYear, CancellationToken token = default)
        {
            using var context = await _contextFactory.CreateDbContextAsync(token);

            // Get all team records for the target year with team info
            var teamRecords = await context.TeamRecords
                .Where(tr => tr.Year == targetYear)
                .Include(tr => tr.Team)
                .ToListAsync(token);

            foreach (var record in teamRecords)
            {
                if (record.Team.Division == "FCS")
                {
                    // FCS teams get NULL for Ranking (no meaningful PR/SOS)
                    record.Ranking = null;
                }
                else
                {
                    // FBS teams: Ranking = WinPct × CombinedSOS × (1 + PowerRating)
                    var totalGames = record.Wins + record.Losses;
                    if (totalGames > 0 && record.CombinedSOS.HasValue && record.PowerRating.HasValue)
                    {
                        var winPct = (decimal)record.Wins / totalGames;
                        var sos = record.CombinedSOS.Value;
                        var pr = record.PowerRating.Value;

                        record.Ranking = Math.Round(winPct * sos * (1 + pr), 4);
                    }
                    else
                    {
                        record.Ranking = null;
                    }
                }
            }

            await context.SaveChangesAsync(token);
        }
    }
}