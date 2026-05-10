using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NCAA_Power_Ratings.Models
{
    [Table("Team")]
    public class Team
    {
        [Key]
        [Column("TeamID")]
        public int TeamID { get; set; }

        [Required]
        [Column("TeamName", TypeName = "varchar(50)")]
        public required string TeamName { get; set; }

        [Column("Alias", TypeName = "varchar(50)")]
        public string? Alias { get; set; }

        [Column("Division", TypeName = "varchar(20)")]
        public string? Division { get; set; }

        [Column("Conference", TypeName = "varchar(50)")]
        public string? Conference { get; set; }

        [Column("ConferenceAbbr", TypeName = "varchar(15)")]
        public string? ConferenceAbbr { get; set; }

        [Column("ShortName", TypeName = "varchar(20)")]
        public string? ShortName { get; set; }
    }
}