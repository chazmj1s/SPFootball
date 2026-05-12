using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SaturdayPulse.Models
{
    [Table("Game")]
    public class Game
    {
       // [Key]
        [Column("Id")]
        public int Id { get; set; }

        [Column("Year")]
        public int Year { get; set; }

        [Column("Week")]
        public int Week { get; set; }

        [Column("GameDate", TypeName = "varchar(20)")]
        public string? GameDate { get; set; }

        [Column("GameDay", TypeName = "varchar(3)")]
        public string? GameDay { get; set; }

        [Column("WinnerId")]
        public int WinnerId { get; set; }

        [Required]
        [Column("WinnerName", TypeName = "varchar(50)")]
        public required string WinnerName { get; set; }

        [Column("WPoints")]
        public int WPoints { get; set; }

        [Column("LoserId")]
        public int LoserId { get; set; }

        [Required]
        [Column("LoserName", TypeName = "varchar(50)")]
        public required string LoserName { get; set; }

        [Column("LPoints")]
        public int LPoints { get; set; }

        [Column("Location")]
        public char Location { get; set; }

        // Derived: true if the game has been played
        [NotMapped]
        public bool IsPlayed => WPoints > 0 || LPoints > 0;

        // Derived: spread
        [NotMapped]
        public int Spread => WPoints - LPoints;

        // Derived: home team is winner when Location == 'W'
        [NotMapped]
        public bool HomeIsWinner => Location == 'W';

        // Derived: neutral site
        [NotMapped]
        public bool NeutralSite => Location == 'N';
    }
}