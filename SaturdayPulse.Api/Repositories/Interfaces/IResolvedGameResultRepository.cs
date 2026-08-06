using SaturdayPulse.Models;

namespace SaturdayPulse.Contracts
{
    /// <summary>
    /// Read-only data access for the ResolvedGameResults view — real result if
    /// the game's been played, otherwise the locked projection. See
    /// ResolvedGameResult remarks for why no per-consumer "which snapshot"
    /// selection is ever needed on top of this.
    /// </summary>
    public interface IResolvedGameResultRepository
    {
        Task<List<ResolvedGameResult>> GetByYearAsync(
            int year, CancellationToken token = default);

        /// <summary>
        /// Every resolved game for a team's season through and including `week`
        /// — the exact shape WeeklyRankingsService needs to build a week's
        /// cumulative Wins/Losses/PointsFor/PointsAgainst.
        /// </summary>
        Task<List<ResolvedGameResult>> GetByYearThroughWeekAsync(
            int year, int week, CancellationToken token = default);
    }
}
