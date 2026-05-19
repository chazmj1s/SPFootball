using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SaturdayPulse.Models
{
    [Table("TeamConferenceHistory")]
    public class TeamConferenceHistory
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        [Column("TeamID")]
        public int TeamID { get; set; }

        [Required]
        [Column("Conference", TypeName = "varchar(50)")]
        public string Conference { get; set; } = string.Empty;

        [Column("ConferenceAbbr", TypeName = "varchar(15)")]
        public string? ConferenceAbbr { get; set; }

        [Required]
        [Column("StartYear")]
        public int StartYear { get; set; }

        [Column("EndYear")]
        public int? EndYear { get; set; }  // NULL = currently in this conference

        // Navigation
        [ForeignKey("TeamID")]
        public Teams? Team { get; set; }
    }
}