using Microsoft.Extensions.Options;
using SaturdayPulse.Configuration;
using SaturdayPulse.Contracts;
using SaturdayPulse.Models;

namespace SaturdayPulse.Services
{
    /// <summary>
    /// Computes and persists per-week power ranking snapshots into WeeklyRankings.
    /// Mirrors the TeamMetricsService pipeline (SOS → PowerRating → Ranking) but
    /// scoped to games played through the specified week.
    /// Also computes per-team offensive and defensive Z-scores.
    /// </summary>
    public class WeeklyRankingsService
    {
        private readonly IUnitOfWork          _uow;
        private readonly MetricsConfiguration _config;

        public WeeklyRankingsService(
            IUnitOfWork uow,
            IOptions<MetricsConfiguration> config)
        {
            _uow    = uow;
            _config = config.Value;
        }

        /// <summary>
        /// Runs the full SOS → PowerRating → Ranking → Offense/Defense pipeline
        /// for all FBS teams through the specified week, then upserts into WeeklyRankings.
        /// </summary>
        public async Task ComputeAndSaveAsync(int year, int week, CancellationToken token = default)
        {
            // ── 1. Load reference data ────────────────────────────────────────────
            var allTeams         = await _uow.Team.GetAllAsync(token);
            var fbsTeams         = allTeams.Where(t => t.Division == "FBS").ToList();
            var fbsIds           = fbsTeams.Select(t => t.TeamID).ToHashSet();
            var avgScoreDeltas   = await _uow.Lookups.GetAvgScoreDeltasAsync(token);
            var matchupHistories = await _uow.Lookups.GetMatchupHistoriesAsync(token);

            // ── 2. Played games through this week ─────────────────────────────────
            var games = await _uow.Game.GetPlayedGamesByYearAndWeekAsync(year, week, token);

            // ── 3. Raw stats per team [wins, losses, pf, pa] ──────────────────────
            var rawStats = fbsTeams.ToDictionary(t => t.TeamID, _ => new int[4]);

            foreach (var g in games)
            {
                if (rawStats.TryGetValue(g.WinnerId, out var ws)) { ws[0]++; ws[2] += g.WPoints; ws[3] += g.LPoints; }
                if (rawStats.TryGetValue(g.LoserId,  out var ls)) { ls[1]++; ls[2] += g.LPoints; ls[3] += g.WPoints; }
            }

            var winsLookup   = rawStats.ToDictionary(kvp => kvp.Key, kvp => kvp.Value[0]);
            var lossesLookup = rawStats.ToDictionary(kvp => kvp.Key, kvp => kvp.Value[1]);

            // ── 4. Game-participant rows (winner + loser perspective) ──────────────
            var teamById = allTeams.ToDictionary(t => t.TeamID);

            var gameParticipants = games
                .Where(g => fbsIds.Contains(g.WinnerId) || fbsIds.Contains(g.LoserId))
                .SelectMany(g => new[]
                {
                    new GameParticipant
                    {
                        TeamId           = g.WinnerId,
                        TeamDivision     = teamById.TryGetValue(g.WinnerId, out var wt)  ? wt.Division : "FBS",
                        OpponentId       = g.LoserId,
                        OpponentDivision = teamById.TryGetValue(g.LoserId,  out var lt)  ? lt.Division : "FBS",
                        TeamPoints       = g.WPoints, OpponentPoints = g.LPoints,
                        Location         = g.Location, IsHomeTeam    = g.Location == 'W'
                    },
                    new GameParticipant
                    {
                        TeamId           = g.LoserId,
                        TeamDivision     = teamById.TryGetValue(g.LoserId,  out var lt2) ? lt2.Division : "FBS",
                        OpponentId       = g.WinnerId,
                        OpponentDivision = teamById.TryGetValue(g.WinnerId, out var wt2) ? wt2.Division : "FBS",
                        TeamPoints       = g.LPoints, OpponentPoints = g.WPoints,
                        Location         = g.Location, IsHomeTeam    = g.Location == 'L'
                    }
                })
                .ToList();

            // ── 5. Z-scores (composite, offensive, defensive) ─────────────────────
            var hfa = _config.HomeFieldAdvantage;
            double leagueAvgScore = games.Count > 0
                ? (games.Average(g => (double)g.WPoints) + games.Average(g => (double)g.LPoints)) / 2.0
                : 28.0;

            var withZScores = gameParticipants.Select(gp =>
            {
                var teamWins   = winsLookup.GetValueOrDefault(gp.TeamId,     0);
                var teamLosses = lossesLookup.GetValueOrDefault(gp.TeamId,   0);
                var oppWins    = winsLookup.GetValueOrDefault(gp.OpponentId, 0);
                var oppLosses  = lossesLookup.GetValueOrDefault(gp.OpponentId, 0);

                var teamWinPct = RatingCalculator.BucketWinPct(teamWins, teamWins + teamLosses);
                var oppWinPct  = RatingCalculator.BucketWinPct(oppWins,  oppWins  + oppLosses);
                var maxWinPct  = Math.Max(teamWinPct, oppWinPct);
                var minWinPct  = Math.Min(teamWinPct, oppWinPct);

                var asd = avgScoreDeltas.FirstOrDefault(
                    a => a.Team1WinPct == maxWinPct && a.Team2WinPct == minWinPct);

                double zScore = 0.0, offZScore = 0.0, defZScore = 0.0;

                if (asd != null && asd.StDevP != 0)
                {
                    var expectedFromTeam = RatingCalculator.ExpectedFromPerspective(
                        (double)asd.AverageScoreDelta, teamWinPct, oppWinPct);
                    expectedFromTeam = RatingCalculator.ApplyHomeField(
                        expectedFromTeam, gp.IsHomeTeam, gp.Location == 'N', hfa);

                    var t1 = Math.Min(gp.TeamId, gp.OpponentId);
                    var t2 = Math.Max(gp.TeamId, gp.OpponentId);
                    var rivalryTier = matchupHistories.FirstOrDefault(
                        m => m.Team1Id == t1 && m.Team2Id == t2)?.RivalryTier;

                    var effectiveStDev = (double)asd.StDevP *
                        RatingCalculator.RivalryVarianceMultiplier(rivalryTier);

                    var delta = gp.TeamPoints - gp.OpponentPoints;
                    zScore    = RatingCalculator.DampenZScore((delta - expectedFromTeam) / effectiveStDev);

                    var expectedTeamScore = leagueAvgScore + (expectedFromTeam / 2.0);
                    var expectedOppScore  = leagueAvgScore - (expectedFromTeam / 2.0);

                    offZScore = RatingCalculator.DampenZScore(
                        (gp.TeamPoints    - expectedTeamScore) / effectiveStDev);
                    defZScore = RatingCalculator.DampenZScore(
                        (expectedOppScore - gp.OpponentPoints) / effectiveStDev);
                }

                var divWeight = RatingCalculator.DivisionWeight(gp.OpponentDivision);

                return new
                {
                    gp.TeamId, gp.TeamDivision, gp.OpponentId, gp.OpponentDivision,
                    ZScore = zScore, OffZScore = offZScore, DefZScore = defZScore,
                    DivWeight = divWeight,
                    PerfWeight = zScore switch { >= 1.0 => 1.25, > -1.0 => 1.00, > -2.0 => 0.75, _ => 0.50 }
                };
            }).ToList();

            // ── 6-8. BaseSOS → SubSOS → CombinedSOS ──────────────────────────────
            var baseSOS = withZScores
                .GroupBy(x => x.TeamId)
                .ToDictionary(g => g.Key, g => Math.Round(
                    g.Sum(x => x.PerfWeight * x.DivWeight) / g.Sum(x => x.DivWeight), 3));

            var subSOS = withZScores
                .GroupBy(x => x.TeamId)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.DivWeight) > 0
                    ? Math.Round(
                        g.Sum(x => baseSOS.GetValueOrDefault(x.OpponentId, 0.0) * x.PerfWeight) /
                        g.Sum(x => x.PerfWeight), 3)
                    : 0.0);

            var combinedSOS = fbsTeams.ToDictionary(t => t.TeamID, t =>
            {
                var b = baseSOS.GetValueOrDefault(t.TeamID, 0.0);
                var s = subSOS.GetValueOrDefault(t.TeamID, b);
                return Math.Round((2 * b + 3 * s) / 5.0, 4);
            });

            // ── 9. PowerRating ────────────────────────────────────────────────────
            var powerRatings = withZScores
                .GroupBy(x => x.TeamId)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.DivWeight) > 0
                    ? Math.Round(
                        g.Sum(x => x.ZScore * x.DivWeight) / g.Sum(x => x.DivWeight) *
                        combinedSOS.GetValueOrDefault(g.Key, 1.0), 4)
                    : 0.0);

            // ── 10. Ranking ───────────────────────────────────────────────────────
            var rankings = fbsTeams.ToDictionary(t => t.TeamID, t =>
            {
                var wins   = winsLookup.GetValueOrDefault(t.TeamID, 0);
                var losses = lossesLookup.GetValueOrDefault(t.TeamID, 0);
                var total  = wins + losses;
                if (total == 0) return (decimal?)null;
                var winPct = (decimal)wins / total;
                var sos    = (decimal)combinedSOS.GetValueOrDefault(t.TeamID, 0.0);
                var pr     = (decimal)powerRatings.GetValueOrDefault(t.TeamID, 0.0);
                return (decimal?)Math.Round(winPct * sos * (1 + pr), 4);
            });

            // ── 11. Overall and tier ranks ────────────────────────────────────────
            var ranked = fbsTeams
                .Where(t => rankings[t.TeamID].HasValue)
                .OrderByDescending(t => rankings[t.TeamID])
                .Select((t, i) => new
                {
                    Team        = t,
                    OverallRank = i + 1,
                    Tier        = RatingCalculator.GetConferenceTier(t.Conference, t.TeamName)
                })
                .ToList();

            var tierRanks = new Dictionary<int, int>();
            foreach (var tierGroup in ranked.GroupBy(x => x.Tier))
            {
                int idx = 1;
                foreach (var x in tierGroup.OrderByDescending(x => rankings[x.Team.TeamID]))
                    tierRanks[x.Team.TeamID] = idx++;
            }

            // ── 12-13. Offensive / defensive Z-scores and ranks ───────────────────
            var offensiveZScores = withZScores.GroupBy(x => x.TeamId).ToDictionary(
                g => g.Key,
                g => g.Sum(x => x.DivWeight) > 0
                    ? Math.Round(g.Sum(x => x.OffZScore * x.DivWeight) / g.Sum(x => x.DivWeight), 4)
                    : 0.0);

            var defensiveZScores = withZScores.GroupBy(x => x.TeamId).ToDictionary(
                g => g.Key,
                g => g.Sum(x => x.DivWeight) > 0
                    ? Math.Round(g.Sum(x => x.DefZScore * x.DivWeight) / g.Sum(x => x.DivWeight), 4)
                    : 0.0);

            var fbsWithGames  = fbsTeams.Where(t => (rawStats[t.TeamID][0] + rawStats[t.TeamID][1]) > 0).ToList();

            var offensiveRanks = fbsWithGames
                .OrderByDescending(t => offensiveZScores.GetValueOrDefault(t.TeamID, 0.0))
                .Select((t, i) => new { t.TeamID, Rank = i + 1 })
                .ToDictionary(x => x.TeamID, x => x.Rank);

            var defensiveRanks = fbsWithGames
                .OrderByDescending(t => defensiveZScores.GetValueOrDefault(t.TeamID, 0.0))
                .Select((t, i) => new { t.TeamID, Rank = i + 1 })
                .ToDictionary(x => x.TeamID, x => x.Rank);

            // ── 14. Upsert into WeeklyRankings ────────────────────────────────────
            var existing = await _uow.Lookups.GetWeeklyRankingsAsync(year, week, token);
            var existingByTeam = existing.ToDictionary(wr => wr.TeamID);

            foreach (var t in fbsTeams)
            {
                var s           = rawStats[t.TeamID];
                var rank        = ranked.FirstOrDefault(r => r.Team.TeamID == t.TeamID);
                var gamesPlayed = s[0] + s[1];

                decimal avgPtsScored  = gamesPlayed > 0 ? Math.Round((decimal)s[2] / gamesPlayed, 2) : 0;
                decimal avgPtsAllowed = gamesPlayed > 0 ? Math.Round((decimal)s[3] / gamesPlayed, 2) : 0;
                decimal offZ          = gamesPlayed > 0 ? (decimal)Math.Round(offensiveZScores.GetValueOrDefault(t.TeamID, 0.0), 4) : 0;
                decimal defZ          = gamesPlayed > 0 ? (decimal)Math.Round(defensiveZScores.GetValueOrDefault(t.TeamID, 0.0), 4) : 0;
                int     offRank       = offensiveRanks.GetValueOrDefault(t.TeamID, 0);
                int     defRank       = defensiveRanks.GetValueOrDefault(t.TeamID, 0);

                if (existingByTeam.TryGetValue(t.TeamID, out var row))
                {
                    row.Wins             = (byte)s[0];
                    row.Losses           = (byte)s[1];
                    row.PointsFor        = s[2];
                    row.PointsAgainst    = s[3];
                    row.BaseSOS          = (decimal?)baseSOS.GetValueOrDefault(t.TeamID);
                    row.SubSOS           = (decimal?)subSOS.GetValueOrDefault(t.TeamID);
                    row.CombinedSOS      = (decimal?)combinedSOS.GetValueOrDefault(t.TeamID);
                    row.PowerRating      = (decimal?)powerRatings.GetValueOrDefault(t.TeamID);
                    row.Ranking          = rankings[t.TeamID];
                    row.OverallRank      = rank?.OverallRank ?? 0;
                    row.TierRank         = tierRanks.GetValueOrDefault(t.TeamID, 0);
                    row.AvgPointsScored  = avgPtsScored;
                    row.AvgPointsAllowed = avgPtsAllowed;
                    row.OffensiveZScore  = offZ;
                    row.DefensiveZScore  = defZ;
                    row.OffensiveRank    = offRank;
                    row.DefensiveRank    = defRank;
                }
                else
                {
                    await _uow.Lookups.AddWeeklyRankingAsync(new WeeklyRanking
                    {
                        TeamID           = t.TeamID,
                        Year             = (short)year,
                        Week             = (byte)week,
                        Wins             = (byte)s[0],
                        Losses           = (byte)s[1],
                        PointsFor        = s[2],
                        PointsAgainst    = s[3],
                        BaseSOS          = (decimal?)baseSOS.GetValueOrDefault(t.TeamID),
                        SubSOS           = (decimal?)subSOS.GetValueOrDefault(t.TeamID),
                        CombinedSOS      = (decimal?)combinedSOS.GetValueOrDefault(t.TeamID),
                        PowerRating      = (decimal?)powerRatings.GetValueOrDefault(t.TeamID),
                        Ranking          = rankings[t.TeamID],
                        OverallRank      = rank?.OverallRank ?? 0,
                        TierRank         = tierRanks.GetValueOrDefault(t.TeamID, 0),
                        AvgPointsScored  = avgPtsScored,
                        AvgPointsAllowed = avgPtsAllowed,
                        OffensiveZScore  = offZ,
                        DefensiveZScore  = defZ,
                        OffensiveRank    = offRank,
                        DefensiveRank    = defRank
                    }, token);
                }
            }

            await _uow.SaveChangesAsync(token);

            // ── 15. Update TeamRecord with latest scoring stats ───────────────────
            // TeamRecord has one row per team per year — always overwrite with the
            // most recently computed week so the final week of any year persists.
            var teamRecords = await _uow.TeamRecords.GetByTeamsAndYearAsync(fbsIds, year, token);

            foreach (var t in fbsTeams)
            {
                if (!teamRecords.TryGetValue(t.TeamID, out var record)) continue;

                var s           = rawStats[t.TeamID];
                var gamesPlayed = s[0] + s[1];
                if (gamesPlayed == 0) continue;

                record.AvgPointsScored  = Math.Round((decimal)s[2] / gamesPlayed, 2);
                record.AvgPointsAllowed = Math.Round((decimal)s[3] / gamesPlayed, 2);
                record.OffensiveZScore  = gamesPlayed > 0 ? (decimal)Math.Round(offensiveZScores.GetValueOrDefault(t.TeamID, 0.0), 4) : 0;
                record.DefensiveZScore  = gamesPlayed > 0 ? (decimal)Math.Round(defensiveZScores.GetValueOrDefault(t.TeamID, 0.0), 4) : 0;
                record.OffensiveRank    = offensiveRanks.GetValueOrDefault(t.TeamID, 0);
                record.DefensiveRank    = defensiveRanks.GetValueOrDefault(t.TeamID, 0);
            }

            await _uow.SaveChangesAsync(token);
        }

        /// <summary>
        /// Backfills all weeks for a given year in chronological order.
        /// </summary>
        public async Task BackfillYearAsync(int year, CancellationToken token = default)
        {
            var playedWeeks = await _uow.Game.GetPlayedWeeksByYearAsync(year, token);

            foreach (var week in playedWeeks)
                await ComputeAndSaveAsync(year, week, token);
        }
    }
}
