using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SaturdayPulse.Models
{
    public class TeamSeasonArcResponse
    {
        public int TeamId { get; set; }
        public string TeamName { get; set; } = string.Empty;
        public int Year { get; set; }
        public List<TeamSeasonWeek> Weeks { get; set; } = new();
    }
}
