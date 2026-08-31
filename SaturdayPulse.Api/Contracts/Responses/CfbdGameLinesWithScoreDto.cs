namespace SaturdayPulse.Contracts.Responses
{
    /// <summary>
    /// Response shape for CFBD GET /lines?gameId=X — confirmed live 2026-08-30
    /// to return homeScore/awayScore embedded alongside the odds array, distinct
    /// from CfbdLinesGameDto (used by GameDataService.LoadLinesAsync's year+week
    /// call), whose field set for score data isn't confirmed. Kept as its own
    /// DTO rather than assuming the two are interchangeable.
    /// </summary>
    public class CfbdGameLinesWithScoreDto
    {
        public int Id { get; set; }
        public int Season { get; set; }
        public string SeasonType { get; set; } = string.Empty;
        public int Week { get; set; }
        public string? StartDate { get; set; }
        public int HomeTeamId { get; set; }
        public string? HomeTeam { get; set; }
        public string? HomeConference { get; set; }
        public string? HomeClassification { get; set; }
        public int? HomeScore { get; set; }
        public int AwayTeamId { get; set; }
        public string? AwayTeam { get; set; }
        public string? AwayConference { get; set; }
        public string? AwayClassification { get; set; }
        public int? AwayScore { get; set; }
        public List<CfbdGameLineDto> Lines { get; set; } = new();
    }

    public class CfbdGameLineDto
    {
        public string Provider { get; set; } = string.Empty;
        public decimal? Spread { get; set; }
        public string? FormattedSpread { get; set; }
        public decimal? SpreadOpen { get; set; }
        public decimal? OverUnder { get; set; }
        public decimal? OverUnderOpen { get; set; }
        public int? HomeMoneyline { get; set; }
        public int? AwayMoneyline { get; set; }
    }
}
