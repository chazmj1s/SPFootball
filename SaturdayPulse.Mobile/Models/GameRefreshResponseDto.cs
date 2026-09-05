namespace SaturdayPulse.Models
{
    /// <summary>
    /// Response shape for GET /api/productiongamedata/game?gameId=X
    /// (ProductionGameDataService.GetGameAsync / ProductionGameDataController).
    /// Deliberately separate from GameLinesDto — this is the raw per-provider
    /// line list returned by the single-game refresh endpoint, not the
    /// aggregated single-line shape used elsewhere in the schedule payload.
    /// </summary>
    public class GameRefreshResponseDto
    {
        public int GameId { get; set; }
        public int HomePoints { get; set; }
        public int AwayPoints { get; set; }
        public string? Status { get; set; }
        public int?    Period { get; set; }
        public string? Clock  { get; set; }
        public List<GameRefreshLineDto> Lines { get; set; } = new();
    }

    public class GameRefreshLineDto
    {
        public string? Provider { get; set; }
        public decimal? Spread { get; set; }
        public string? FormattedSpread { get; set; }
        public decimal? OverUnder { get; set; }
        public int? HomeMoneyline { get; set; }
        public int? AwayMoneyline { get; set; }
    }
}
