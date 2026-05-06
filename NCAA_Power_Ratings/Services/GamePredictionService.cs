using Microsoft.EntityFrameworkCore;
using NCAA_Power_Ratings.Data;
using NCAA_Power_Ratings.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NCAA_Power_Ratings.Services
{
    /// <summary>
    /// Service for predicting game scores based on team metrics and historical data.
    /// </summary>
    public class GamePredictionService
    {
        private readonly IDbContextFactory<NCAAContext> _contextFactory;
        private const double HomeFieldAdvantage = 2.5;
        private const int RecentYearsForAverage = 5; // Use last 5 years for average scoring
        private double? _cachedAvgTeamScore = null; // Cache the calculated average

        public GamePredictionService(IDbContextFactory<NCAAContext> contextFactory)
        {
            _contextFactory = contextFactory;
        }

        /// <summary>
        /// Calculates actual average points per team from recent game history.
        /// Cached after first calculation.
        /// </summary>
        private async Task<double> GetAverageTeamScore(NCAAContext context, CancellationToken token = default)
        {
            if (_cachedAvgTeamScore.HasValue)
                return _cachedAvgTeamScore.Value;

            var cutoffYear = DateTime.Now.Year - RecentYearsForAverage;

            var games = await context.Games
                .Where(g => g.Year >= cutoffYear)
                .ToListAsync(token);

            if (games.Count == 0)
            {
                _cachedAvgTeamScore = 28.0; // Fallback if no data
                return 28.0;
            }

            // Average of all winning and losing scores
            _cachedAvgTeamScore = (games.Average(g => g.WPoints) + games.Average(g => g.LPoints)) / 2.0;

            return _cachedAvgTeamScore.Value;
        }

        /// <summary>
        /// Predicts the score for a single matchup between two teams.
        /// </summary>
        public async Task<GamePrediction> PredictMatchup(
            int year, 
            string teamName, 
            string opponentName, 
            char location, // 'H' = team home, 'A' = team away, 'N' = neutral
            int week = 0,
            CancellationToken token = default)
        {
            using var context = await _contextFactory.CreateDbContextAsync(token);

            // Load necessary data
            var teams = await context.Teams.ToListAsync(token);
            var team = teams.FirstOrDefault(t => t.TeamName == teamName);
            var opponent = teams.FirstOrDefault(t => t.TeamName == opponentName);

            if (team == null || opponent == null)
            {
                throw new ArgumentException("One or both teams not found");
            }

            var teamRecords = await context.TeamRecords
                .Where(tr => tr.Year == year && (tr.TeamID == team.TeamID || tr.TeamID == opponent.TeamID))
                .ToDictionaryAsync(tr => tr.TeamID, token);

            if (!teamRecords.TryGetValue(team.TeamID, out var teamRecord) ||
                !teamRecords.TryGetValue(opponent.TeamID, out var oppRecord))
            {
                throw new ArgumentException("Team records not found for specified year");
            }

            var avgScoreDeltas = await context.AvgScoreDeltas.ToListAsync(token);
            var rivalries = await context.MatchupHistories.ToListAsync(token);

            // Get actual average scoring from recent history
            var avgTeamScore = await GetAverageTeamScore(context, token);

            return CalculatePrediction(
                teamRecord, oppRecord, team, opponent, location, 
                avgScoreDeltas, rivalries, avgTeamScore, year, week);
        }

        /// <summary>
        /// Predicts scores for multiple matchups provided as a list.
        /// </summary>
        public async Task<List<GamePrediction>> PredictMatchups(
            int year,
            List<MatchupRequest> matchups,
            CancellationToken token = default)
        {
            using var context = await _contextFactory.CreateDbContextAsync(token);

            // Load all necessary data once
            var teams = await context.Teams.ToDictionaryAsync(t => t.TeamName, token);
            var teamRecords = await context.TeamRecords
                .Where(tr => tr.Year == year)
                .ToDictionaryAsync(tr => tr.TeamID, token);
            var avgScoreDeltas = await context.AvgScoreDeltas.ToListAsync(token);
            var rivalries = await context.MatchupHistories.ToListAsync(token);

            // Get actual average scoring from recent history
            var avgTeamScore = await GetAverageTeamScore(context, token);

            var predictions = new List<GamePrediction>();

            foreach (var matchup in matchups)
            {
                if (!teams.TryGetValue(matchup.TeamName, out var team) ||
                    !teams.TryGetValue(matchup.OpponentName, out var opponent))
                {
                    continue; // Skip invalid matchup
                }

                if (!teamRecords.TryGetValue(team.TeamID, out var teamRecord) ||
                    !teamRecords.TryGetValue(opponent.TeamID, out var oppRecord))
                {
                    continue; // Skip if records not found
                }

                var prediction = CalculatePrediction(
                    teamRecord, oppRecord, team, opponent, matchup.Location,
                    avgScoreDeltas, rivalries, avgTeamScore, year, matchup.Week);

                predictions.Add(prediction);
            }

            return predictions.OrderByDescending(p => Math.Abs(p.ExpectedMargin)).ToList();
        }

        /// <summary>
        /// Core prediction calculation logic.
        /// </summary>
        private GamePrediction CalculatePrediction(
            TeamRecord teamRecord,
            TeamRecord oppRecord,
            Team team,
            Team opponent,
            char location,
            List<AvgScoreDelta> avgScoreDeltas,
            List<MatchupHistory> rivalries,
            double avgTeamScore,
            int year,
            int week)
        {
            // Calculate win percentages (round to 0.05 increments for 5% buckets)
            var teamGamesPlayed = teamRecord.Wins + teamRecord.Losses;
            var oppGamesPlayed = oppRecord.Wins + oppRecord.Losses;

            var teamWinPct = teamGamesPlayed > 0 
                ? Math.Round((decimal)teamRecord.Wins / teamGamesPlayed * 20m, MidpointRounding.AwayFromZero) / 20m
                : 0m;
            var oppWinPct = oppGamesPlayed > 0 
                ? Math.Round((decimal)oppRecord.Wins / oppGamesPlayed * 20m, MidpointRounding.AwayFromZero) / 20m
                : 0m;

            var maxWinPct = Math.Max(teamWinPct, oppWinPct);
            var minWinPct = Math.Min(teamWinPct, oppWinPct);

            // Get expected score delta from historical data
            var asd = avgScoreDeltas.FirstOrDefault(
                a => a.Team1WinPct == maxWinPct && a.Team2WinPct == minWinPct);

            if (asd == null || asd.SampleSize < 10)
            {
                // No historical data or small sample size, use reasonable default
                asd = new AvgScoreDelta
                {
                    Team1WinPct = maxWinPct,
                    Team2WinPct = minWinPct,
                    AverageScoreDelta = 7.0m,
                    StDevP = 14.0m
                };
            }

            // Base expected delta (from higher-win team's perspective)
            // Cap at ±35 points to prevent outlier blowouts from dominating
            var expectedDelta = Math.Max(-35.0, Math.Min(35.0, (double)asd.AverageScoreDelta));

            // Adjust to team's perspective
            var deltaFromTeamPerspective = teamWinPct >= oppWinPct 
                ? expectedDelta 
                : -expectedDelta;

            // Adjust for home field advantage
            if (location == 'H') // Team is home
                deltaFromTeamPerspective += HomeFieldAdvantage;
            else if (location == 'A') // Team is away
                deltaFromTeamPerspective -= HomeFieldAdvantage;
            // Neutral site: no adjustment

            // Adjust for team quality (PowerRating difference)
            if (teamRecord.PowerRating.HasValue && oppRecord.PowerRating.HasValue)
            {
                var prDiff = (double)(teamRecord.PowerRating.Value - oppRecord.PowerRating.Value);
                // Scale PR difference to points (conservative: 0.1 PR = 1 point)
                // This makes PR a minor adjustment rather than dominating factor
                deltaFromTeamPerspective += prDiff * 10.0;
            }

            // Check for rivalry variance (increases uncertainty moderately)
            var normalizedTeam1 = Math.Min(team.TeamID, opponent.TeamID);
            var normalizedTeam2 = Math.Max(team.TeamID, opponent.TeamID);
            var rivalry = rivalries.FirstOrDefault(r => 
                r.Team1Id == normalizedTeam1 && r.Team2Id == normalizedTeam2);

            double varianceMultiplier = 1.0;
            string rivalryNote = null;
            if (rivalry != null)
            {
                // More conservative rivalry multipliers
                varianceMultiplier = rivalry.RivalryTier switch
                {
                    "EPIC" => 1.3,      // 30% more uncertainty
                    "NATIONAL" => 1.2,  // 20% more uncertainty
                    "STATE" => 1.1,     // 10% more uncertainty
                    "MEH" => 1.0,       // No change
                    _ => 1.0
                };
                rivalryNote = $"{rivalry.RivalryName} ({rivalry.RivalryTier})";
            }

            // Calculate predicted scores using team-specific offensive/defensive performance
            // Team's offense (PPG) vs Opponent's defense (PAG)
            // Use actual games played (Wins + Losses), not full season count
            var teamGamesPlayedDouble = (double)teamGamesPlayed;
            var oppGamesPlayedDouble = (double)oppGamesPlayed;

            var teamPPG = teamGamesPlayedDouble > 0 ? teamRecord.PointsFor / teamGamesPlayedDouble : 28.0;
            var oppPPG = oppGamesPlayedDouble > 0 ? oppRecord.PointsFor / oppGamesPlayedDouble : 28.0;
            var teamPAG = teamGamesPlayedDouble > 0 ? teamRecord.PointsAgainst / teamGamesPlayedDouble : 28.0;
            var oppPAG = oppGamesPlayedDouble > 0 ? oppRecord.PointsAgainst / oppGamesPlayedDouble : 28.0;

            // Base expected score: blend of team's offensive output and opponent's defensive performance
            var teamBaseScore = (teamPPG + oppPAG) / 2.0;
            var oppBaseScore = (oppPPG + teamPAG) / 2.0;

            // Apply expected margin adjustment on top of team-specific baselines
            var predictedTeamScore = teamBaseScore + (deltaFromTeamPerspective / 2.0);
            var predictedOppScore = oppBaseScore - (deltaFromTeamPerspective / 2.0);

            // Apply week-based scoring adjustment (early season higher, late season lower)
            double weekMultiplier = week switch
            {
                <= 4 => 1.05,    // Early season: offenses ahead of defenses
                >= 11 => 0.95,   // Late season: defenses optimized, weather factors
                _ => 1.0         // Mid season: normal
            };

            predictedTeamScore *= weekMultiplier;
            predictedOppScore *= weekMultiplier;

            // Apply dynamic scoring adjustment based on game context
            var scoringAdjustment = CalculateScoringAdjustment(
                teamRecord, 
                oppRecord, 
                rivalry, 
                week
            );

            predictedTeamScore *= scoringAdjustment;
            predictedOppScore *= scoringAdjustment;

            // Ensure scores never go negative (floor at 0)
            predictedTeamScore = Math.Max(0, predictedTeamScore);
            predictedOppScore = Math.Max(0, predictedOppScore);

            // Apply variance for prediction range
            var stdDev = (double)asd.StDevP * varianceMultiplier;

            // Cap margin of error at 21 points (3 TDs) for practical predictions
            // Ensure minimum margin of error of 7 points (at least one TD variance)
            var marginOfError = Math.Min(Math.Max(stdDev * 1.0, 7.0), 21.0);

            return new GamePrediction
            {
                GameId = 0, // No game ID for predictions
                Week = week,
                TeamName = team.TeamName,
                OpponentName = opponent.TeamName,
                Location = location,
                TeamWins = (int)teamRecord.Wins,
                OpponentWins = (int)oppRecord.Wins,
                PredictedTeamScore = Math.Round(predictedTeamScore, 1),
                PredictedOpponentScore = Math.Round(predictedOppScore, 1),
                ExpectedMargin = Math.Round(deltaFromTeamPerspective, 1),
                MarginOfError = Math.Round(marginOfError, 1),
                RawStdDev = stdDev, // Unclamped — used for win probability math
                Confidence = CalculateConfidence(stdDev, varianceMultiplier),
                RivalryNote = rivalryNote,
                TeamPowerRating = teamRecord.PowerRating,
                OpponentPowerRating = oppRecord.PowerRating
            };
        }

        /// <summary>
        /// Calculates confidence level based on standard deviation and rivalry variance.
        /// Lower variance = higher confidence.
        /// </summary>
        private string CalculateConfidence(double stdDev, double varianceMultiplier)
        {
            var adjustedStDev = stdDev * varianceMultiplier;

            if (adjustedStDev < 10) return "High";
            if (adjustedStDev < 14) return "Medium";
            if (adjustedStDev < 18) return "Low";
            return "Very Low";
        }

        /// <summary>
        /// Calculates dynamic scoring adjustment based on game context.
        /// Returns a multiplier to apply to predicted scores (e.g., 0.92 = 8% reduction).
        /// </summary>
        private double CalculateScoringAdjustment(
            TeamRecord teamRecord,
            TeamRecord oppRecord,
            MatchupHistory rivalry,
            int week)
        {
            double adjustment = 1.0;

            // 1. Rivalry game defensive intensity
            if (rivalry != null)
            {
                adjustment *= rivalry.RivalryTier switch
                {
                    "EPIC" => 0.90,      // Epic rivalries: 10% lower scoring
                    "NATIONAL" => 0.93,  // National rivalries: 7% lower scoring
                    "STATE" => 0.95,     // State rivalries: 5% lower scoring
                    _ => 1.0
                };
            }

            // 2. Top-25 matchup (both teams ranked)
            if (teamRecord.Ranking.HasValue && teamRecord.Ranking <= 25 &&
                oppRecord.Ranking.HasValue && oppRecord.Ranking <= 25)
            {
                // Big games tend to be defensive battles
                adjustment *= 0.95; // Additional 5% reduction
            }

            // 3. Championship week intensity
            if (week >= 15)
            {
                // Conference championships and playoffs: tighter defense
                adjustment *= 0.93; // Additional 7% reduction
            }

            return adjustment;
        }
    }

    /// <summary>
    /// Request object for a single matchup prediction.
    /// </summary>
    public class MatchupRequest
    {
        public string TeamName { get; set; }
        public string OpponentName { get; set; }
        public char Location { get; set; } // 'H', 'A', or 'N'
        public int Week { get; set; }
    }

    /// <summary>
    /// Represents a predicted game outcome.
    /// </summary>
    public class GamePrediction
    {
        public int GameId { get; set; }
        public int Week { get; set; }
        public string TeamName { get; set; }
        public string OpponentName { get; set; }
        public char Location { get; set; }
        public int TeamWins { get; set; }
        public int OpponentWins { get; set; }
        public double PredictedTeamScore { get; set; }
        public double PredictedOpponentScore { get; set; }
        public double ExpectedMargin { get; set; }
        public double MarginOfError { get; set; }

        /// <summary>
        /// Unclamped standard deviation (stdDev * varianceMultiplier) used for win
        /// probability. Distinct from MarginOfError which is capped at [7, 21] for display.
        /// </summary>
        public double RawStdDev { get; set; }

        public string Confidence { get; set; }
        public string RivalryNote { get; set; }
        public decimal? TeamPowerRating { get; set; }
        public decimal? OpponentPowerRating { get; set; }

        public string LocationDisplay => Location switch
        {
            'H' => "vs",
            'A' => "@",
            'N' => "N",
            _ => ""
        };

        /// <summary>
        /// Win probability for TeamName as 0.0–1.0.
        /// Uses the same normal CDF conversion a sportsbook uses to go from a
        /// point spread to a moneyline — just expressed as a percentage.
        ///
        ///   ExpectedMargin > 0  → favored   → WinProbability > 0.50
        ///   ExpectedMargin == 0 → pick 'em  → WinProbability == 0.50
        ///   ExpectedMargin < 0  → underdog  → WinProbability < 0.50
        /// </summary>
        public double WinProbability
        {
            get
            {
                // Floor at 7 matches the MarginOfError minimum and prevents
                // extreme probabilities from very small sample sizes.
                var sigma = Math.Max(RawStdDev, 7.0);
                return NormalCdf(ExpectedMargin / sigma);
            }
        }

        /// <summary>Win probability for the opponent (always 1 - WinProbability).</summary>
        public double OpponentWinProbability => 1.0 - WinProbability;

        /// <summary>WinProbability as a display string, e.g. "67%".</summary>
        public string WinProbabilityDisplay => $"{WinProbability:P0}";

        /// <summary>OpponentWinProbability as a display string, e.g. "33%".</summary>
        public string OpponentWinProbabilityDisplay => $"{OpponentWinProbability:P0}";

        public string PredictionSummary =>
            $"{TeamName} {PredictedTeamScore:F1} {LocationDisplay} {OpponentName} {PredictedOpponentScore:F1} " +
            $"(±{MarginOfError:F1}, {Confidence} confidence, {WinProbabilityDisplay})";

        // ── Normal CDF ───────────────────────────────────────────────────────────
        // Abramowitz & Stegun approximation (26.2.17). Accurate to ~7 decimal places.

        private static double NormalCdf(double z)
        {
            const double p  =  0.2316419;
            const double b1 =  0.319381530;
            const double b2 = -0.356563782;
            const double b3 =  1.781477937;
            const double b4 = -1.821255978;
            const double b5 =  1.330274429;

            bool negative = z < 0;
            z = Math.Abs(z);

            double t    = 1.0 / (1.0 + p * z);
            double poly = t * (b1 + t * (b2 + t * (b3 + t * (b4 + t * b5))));
            double pdf  = Math.Exp(-0.5 * z * z) / Math.Sqrt(2 * Math.PI);
            double cdf  = 1.0 - pdf * poly;

            return negative ? 1.0 - cdf : cdf;
        }
    }
}
