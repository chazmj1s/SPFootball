using System.ComponentModel.DataAnnotations;

namespace SaturdayPulse.Models
{
    public class YearlySpreadMultiplier
    {
        [Key]
        public int Targetyear { get; set; }
        public double Multiplier { get; set; }
    }
}
