using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SaturdayPulse.Models
{
    [Table("TeamsConferenceHistory")]
    public class TeamsConferenceHistory
    {
        [Key]
        public int Id { get; set; }
        public int TeamId { get; set; }
        public int ConferenceId { get; set; }
        public int StartYear { get; set; }
        public int? EndYear { get; set; }
    }
}
