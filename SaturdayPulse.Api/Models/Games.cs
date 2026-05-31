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
    }
}
