using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SaturdayPulse.Models
{
    [Table("Lines")]
    public class Lines
    {
        [Key]
        public int Id { get; set; }
        public int GameId { get; set; }                  // CFBD game id → Games.GameId
        public string Provider { get; set; } = string.Empty;
        public decimal? Spread { get; set; }
        public decimal? SpreadOpen { get; set; }
        public string? FormattedSpread { get; set; }
        public decimal? OverUnder { get; set; }
        public decimal? OverUnderOpen { get; set; }
        public int? HomeMoneyline { get; set; }
        public int? AwayMoneyline { get; set; }
    }
}
