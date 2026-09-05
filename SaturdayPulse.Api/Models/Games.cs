using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SaturdayPulse.Models
{
    [Table("Games")]
    public class Games
    {
        [Key]
        public int Id { get; set; }
        public int GameId { get; set; }
        public int Year { get; set; }
        public int Week { get; set; }
        public string SeasonType { get; set; } = "regular";
        public string? GameDate { get; set; }
        public string? KickoffTime { get; set; }
        public string? GameDay { get; set; }
        public int? HomeId { get; set; }
        public string? HomeName { get; set; }
        public int? HomePoints { get; set; }
        public int? AwayId { get; set; }
        public string? AwayName { get; set; }
        public int? AwayPoints { get; set; }
        public bool NeutralSite { get; set; }
        public bool ConferenceGame { get; set; }
        public int? Attendance { get; set; }
        public string? Venue      { get; set; }
        public int? CfpSeed { get; set; }

        // Live status (2026-09-05) — written by GameScorePollingService from
        // CFBD's /scoreboard "status"/"period"/"clock" fields (confirmed
        // against a real sample: top-level keys, not nested under a
        // "current*" prefix). Transient during a game, but persisted here
        // rather than flowed through only, since GameScorePollingService is
        // the only path that keeps schedule/v2 current between manual
        // refreshes — same reasoning as HomePoints/AwayPoints.
        public string? Status { get; set; }
        public int?    Period { get; set; }
        public string? Clock  { get; set; }
    }
}
