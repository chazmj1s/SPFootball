using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SaturdayPulse.Models
{
    [Table("Conferences")]
    public class Conference
    {
        [Key]
        public int Id { get; set; }
        public int ConferenceId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? ShortName { get; set; }
        public string? Abbreviation { get; set; }
        public string? Classification { get; set; }
    }
}
