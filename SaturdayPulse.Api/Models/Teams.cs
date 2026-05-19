using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SaturdayPulse.Models
{
    [Table("Teams")]
    public class Teams
    {
        [Key]
        public int Id { get; set; }
        public int TeamId { get; set; }
        public string TeamName { get; set; } = string.Empty;
        public string? Mascot { get; set; }
        public string? Abbreviation { get; set; }
        public string? Alias { get; set; }
        public string? Division { get; set; }
        public int? ConferenceId { get; set; }
        public string? ShortName { get; set; }

        [ForeignKey("ConferenceID")]
        public Conference? Conference { get; set; }
    }
}
