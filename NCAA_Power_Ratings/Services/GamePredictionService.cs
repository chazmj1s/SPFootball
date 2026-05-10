using NCAA_Power_Ratings.Contracts;
using NCAA_Power_Ratings.Contracts.Requests;
using NCAA_Power_Ratings.Contracts.Responses;
using NCAA_Power_Ratings.Models;

namespace NCAA_Power_Ratings.Services
{
    /// <summary>
    /// Predicts game scores based on team metrics and historical data.
    /// Uses IUnitOfWork for all data access — no direct EF/DbContext references.
    /// Statistical calculation logic is unchanged.
    /// </summary>
    public class GamePredictionService
    {
        private readonly IUnitOfWork _uow;
        private const    double      HomeFieldAdvantage    = 2.5;
        private const    int         RecentYearsForAverage = 5;
        private          double?     _cachedAvgTeamScore;

        public GamePredictionService(IUnitOfWork uow) => _uow = uow;

        // ── Public API ────────────────────────────────────────────────────────────

        /// <summary>Predicts the score for a single matchup.</summary>
        public async Task<GamePrediction> PredictMatchup(
            int year, string teamName, string opponentName,
            char location, int week = 0, CancellationToken token = default)
        {
            var team     = await _uow.Teams.GetByNameAsync(teamName, token)
                           ?? throw new ArgumentException($"Team not found: {teamName}");
            var opponent = await _uow.Teams.GetByNameAsync(opponentName, token)
                           ?? throw new ArgumentException($"Team not found: {opponentName}");

            var teamRecords = await _uow.TeamRecords.GetByTeamsAndYearAsync(
                new[] { team.TeamID, opponent.TeamID }, year, token);

            if (!teamRecords.TryGetValue(team.TeamID,     out var teamRecord) ||
                !teamRecords.TryGetValue(opponent.TeamID, out var oppRecord))
                throw new ArgumentException("Team records not found for specified year.");

            var avgScoreDeltas = await _uow.Lookups.GetAvgScoreDeltasAsync(token);
            var rivalries      = await _uow.Lookups.GetMatchupHistoriesAsync(token);
            var avgTeamScore   = await GetAverageTeamScoreAsync(year, token);

            return CalculatePrediction(
                teamRecord, oppRecord, team, opponent, location,
                avgScoreDeltas, rivalries, avgTeamScore, year, week);
        }

        /// <summary>Predicts scores for multiple matchups in a single DB round-trip.</summary>
        public async Task<List<GamePrediction>> PredictMatchups(
            int year, List<MatchupRequest> matchups, CancellationToken token = default)
        {
            var teams          = await _uow.Teams.GetTeamDictionaryByNameAsync(token);
            var teamRecords    = await _uow.TeamRecords.GetByYearAsync(year, token);
            var recordsById    = teamRecords.ToDictionary(tr => tr.TeamID);
            var avgScoreDeltas = await _uow.Lookups.GetAvgScoreDeltasAsync(token);
            var rivalries      = await _uow.Lookups.GetMatchupHistoriesAsync(token);
            var avgTeamScore   = await GetAverageTeamScoreAsync(year, token);

            var predictions = new List<GamePrediction>();

            foreach (var matchup in matchups)
            {
                if (!teams.TryGetValue(matchup.TeamName,     out var team)      ||
                    !teams.TryGetValue(matchup.OpponentName, out var opponent))  continue;

                if (!recordsById.TryGetValue(team.TeamID,     out var teamRecord) ||
                    !recordsById.TryGetValue(opponent.TeamID, out var oppRecord))  continue;

                predictions.Add(CalculatePrediction(
                    teamRecord, oppRecord, team, opponent, matchup.Location,
                    avgScoreDeltas, rivalries, avgTeamScore, year, matchup.Week));
            }

            return predictions.OrderByDescending(p => Math.Abs(p.ExpectedMargin)).ToList();
        }

        // ── Core prediction ───────────────────────────────────────────────────────

        private GamePrediction CalculatePrediction(
            TeamRecord teamRecord, TeamRecord oppRecord,
            Team team, Team opponent,
            char location,
            List<AvgScoreDelta> avgScoreDeltas,
            List<MatchupHistory> rivalries,
            double avgTeamScore,
            int year, int week)
        {
            var teamGamesPlayed = teamRecord.Wins + teamRecord.Losses;
            var oppGamesPlayed  = oppRecord.Wins  + oppRecord.Losses;

            var teamWinPct = RatingCalculator.BucketWinPct(teamRecord.Wins, teamGamesPlayed);
            var oppWinPct  = RatingCalculator.BucketWinPct(oppRecord.Wins,  oppGamesPlayed);
            var maxWinPct  = Math.Max(teamWinPct, oppWinPct);
            var minWinPct  = Math.Min(teamWinPct, oppWinPct);

            var asd = avgScoreDeltas.FirstOrDefault(
                          a => a.Team1WinPct == maxWinPct && a.Team2WinPct == minWinPct)
                      ?? new AvgScoreDelta
                         {
                             Team1WinPct       = maxWinPct,
                             Team2WinPct       = minWinPct,
                             AverageScoreDelta = 7.0m,
                             StDevP            = 14.0m
                         };

            var rawExpected      = Math.Max(-35.0, Math.Min(35.0, (double)asd.AverageScoreDelta));
            var expectedFromTeam = RatingCalculator.ExpectedFromPerspective(rawExpected, teamWinPct, oppWinPct);
            expectedFromTeam     = RatingCalculator.ApplyHomeField(
                expectedFromTeam, location == 'H', location == 'N', HomeFieldAdvantage);

            if (teamRecord.PowerRating.HasValue && oppRecord.PowerRating.HasValue)
                expectedFromTeam += (double)(teamRecord.PowerRating.Value - oppRecord.PowerRating.Value) * 10.0;

            var normalizedT1 = Math.Min(team.TeamID, opponent.TeamID);
            var normalizedT2 = Math.Max(team.TeamID, opponent.TeamID);
            var rivalry      = rivalries.FirstOrDefault(
                r => r.Team1Id == normalizedT1 && r.Team2Id == normalizedT2);

            double  varianceMultiplier = RatingCalculator.RivalryVarianceMultiplierForDisplay(rivalry?.RivalryTier);
            string? rivalryNote        = rivalry != null
                ? $"{rivalry.RivalryName} ({rivalry.RivalryTier})" : null;

            var teamPPG = teamGamesPlayed > 0 ? teamRecord.PointsFor     / (double)teamGamesPlayed : 28.0;
            var oppPPG  = oppGamesPlayed  > 0 ? oppRecord.PointsFor      / (double)oppGamesPlayed  : 28.0;
            var teamPAG = teamGamesPlayed > 0 ? teamRecord.PointsAgainst / (double)teamGamesPlayed : 28.0;
            var oppPAG  = oppGamesPlayed  > 0 ? oppRecord.PointsAgainst  / (double)oppGamesPlayed  : 28.0;

            var predictedTeamScore = (teamPPG + oppPAG) / 2.0 + (expectedFromTeam / 2.0);
            var predictedOppScore  = (oppPPG  + teamPAG) / 2.0 - (expectedFromTeam / 2.0);

            double weekMultiplier = week switch { <= 4 => 1.05, >= 11 => 0.95, _ => 1.0 };
            predictedTeamScore   *= weekMultiplier;
            predictedOppScore    *= weekMultiplier;

            double scoringAdjustment = RatingCalculator.RivalryScoringAdjustment(rivalry?.RivalryTier);
            if (teamRecord.Ranking.HasValue && teamRecord.Ranking <= 25 &&
                oppRecord.Ranking.HasValue  && oppRecord.Ranking  <= 25)
                scoringAdjustment *= 0.95;
            if (week >= 15) scoringAdjustment *= 0.93;

            predictedTeamScore = Math.Max(0, predictedTeamScore * scoringAdjustment);
            predictedOppScore  = Math.Max(0, predictedOppScore  * scoringAdjustment);

            var stdDev        = (double)asd.StDevP * varianceMultiplier;
            var marginOfError = Math.Min(Math.Max(stdDev, 7.0), 21.0);

            return new GamePrediction
            {
                GameId                 = 0,
                Week                   = week,
                TeamName               = team.TeamName,
                OpponentName           = opponent.TeamName,
                Location               = location,
                TeamWins               = (int)teamRecord.Wins,
                OpponentWins           = (int)oppRecord.Wins,
                PredictedTeamScore     = Math.Round(predictedTeamScore, 1),
                PredictedOpponentScore = Math.Round(predictedOppScore,  1),
                ExpectedMargin         = Math.Round(expectedFromTeam,   1),
                MarginOfError          = Math.Round(marginOfError,      1),
                RawStdDev              = stdDev,
                Confidence             = CalculateConfidence(stdDev, varianceMultiplier),
                RivalryNote            = rivalryNote,
                TeamPowerRating        = teamRecord.PowerRating,
                OpponentPowerRating    = oppRecord.PowerRating
            };
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private async Task<double> GetAverageTeamScoreAsync(int year, CancellationToken token)
        {
            if (_cachedAvgTeamScore.HasValue) return _cachedAvgTeamScore.Value;

            var cutoffYear = year - RecentYearsForAverage;
            var games      = await _uow.Games.GetPlayedGamesSinceYearAsync(cutoffYear, token);

            _cachedAvgTeamScore = games.Count == 0
                ? 28.0
                : (games.Average(g => g.WPoints) + games.Average(g => g.LPoints)) / 2.0;

            return _cachedAvgTeamScore.Value;
        }

        private static string CalculateConfidence(double stdDev, double varianceMultiplier)
        {
            var adjusted = stdDev * varianceMultiplier;
            if (adjusted < 10) return "High";
            if (adjusted < 14) return "Medium";
            if (adjusted < 18) return "Low";
            return "Very Low";
        }
    }
}
