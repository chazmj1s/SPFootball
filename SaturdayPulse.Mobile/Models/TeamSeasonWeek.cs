using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SaturdayPulse.Models
{
    public class TeamSeasonWeek
    {
        public int Week { get; set; }
        public double? Ranking { get; set; }
        public double? CombinedSOS { get; set; }
        public double WinPct { get; set; }
        public int Wins { get; set; }
        public int Losses { get; set; }
    }
}
