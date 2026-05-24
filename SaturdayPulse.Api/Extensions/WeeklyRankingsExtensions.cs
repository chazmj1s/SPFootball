using SaturdayPulse.Models;

namespace SaturdayPulse.Extensions
{
    public static class WeeklyRankingsExtensions
    {
        public static List<TeamRecord> ToTeamRecords(this IEnumerable<WeeklyRanking> rankings)
        {
            return rankings.Select(r => r.ToTeamRecord()).ToList();
        }
        public static TeamRecord ToTeamRecord(this WeeklyRanking ranking)
        {
            return new TeamRecord
            {
                TeamID = ranking.TeamID,
                Year = ranking.Year,
                Wins = ranking.Wins,
                Losses = ranking.Losses,
                PointsFor = ranking.PointsFor,
                PointsAgainst = ranking.PointsAgainst,
                BaseSOS = ranking.BaseSOS,
                SubSOS = ranking.SubSOS,
                CombinedSOS = ranking.CombinedSOS,
                PowerRating = ranking.PowerRating,
                Ranking = ranking.Ranking,
                AvgPointsScored = ranking.AvgPointsScored,
                AvgPointsAllowed = ranking.AvgPointsAllowed,
                OffensiveZScore = ranking.OffensiveZScore,
                DefensiveZScore = ranking.DefensiveZScore,
                OffensiveRank = ranking.OffensiveRank,
                DefensiveRank = ranking.DefensiveRank
            };
        }

        public static void UpdateTeamRecords(this IEnumerable<WeeklyRanking> rankings, IDictionary<int, TeamRecord> existingRecords)
        {
            foreach (var ranking in rankings)
            {
                if (existingRecords.TryGetValue(ranking.TeamID, out var record))
                {
                    ranking.UpdateTeamRecord(record);
                }
            }
        }
        public static void UpdateTeamRecord(this WeeklyRanking source, TeamRecord target)
        {
            target.Wins = source.Wins;
            target.Losses = source.Losses;
            target.PointsFor = source.PointsFor;
            target.PointsAgainst = source.PointsAgainst;
            target.BaseSOS = source.BaseSOS;
            target.SubSOS = source.SubSOS;
            target.CombinedSOS = source.CombinedSOS;
            target.PowerRating = source.PowerRating;
            target.Ranking = source.Ranking;
            target.AvgPointsScored = source.AvgPointsScored;
            target.AvgPointsAllowed = source.AvgPointsAllowed;
            target.OffensiveZScore = source.OffensiveZScore;
            target.DefensiveZScore = source.DefensiveZScore;
            target.OffensiveRank = source.OffensiveRank;
            target.DefensiveRank = source.DefensiveRank;
        }
    }
}
