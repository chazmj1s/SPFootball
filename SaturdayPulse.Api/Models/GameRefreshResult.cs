namespace SaturdayPulse.Models
{
    /// <summary>
    /// Result of a manual single-game refresh (ProductionGameDataService.GetGameAsync).
    /// Games is the post-refresh row for the requested gameId; Lines is that
    /// game's current line set (may be empty if CFBD has no lines posted for
    /// this game/week yet — that's a valid outcome, not a failure).
    /// </summary>
    public class GameRefreshResult
    {
        public required Games Games { get; init; }
        public required List<Lines> Lines { get; init; }
    }
}
