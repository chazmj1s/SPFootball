using System;
using System.Collections.Generic;

namespace SaturdayPulse.TempModels;

public partial class Team
{
    public int TeamId { get; set; }

    public string TeamName { get; set; } = null!;

    public string? Alias { get; set; }

    public string? Division { get; set; }

    public string? Conference { get; set; }

    public string? ConferenceAbbr { get; set; }

    public virtual ICollection<TeamRecord> TeamRecords { get; set; } = new List<TeamRecord>();
}
