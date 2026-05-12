using System;
using System.Collections.Generic;

namespace SaturdayPulse.TempModels;

public partial class TeamRecord
{
    public int Id { get; set; }

    public int TeamId { get; set; }

    public short Year { get; set; }

    public byte Wins { get; set; }

    public byte Losses { get; set; }

    public int PointsFor { get; set; }

    public int PointsAgainst { get; set; }

    public double? BaseSos { get; set; }

    public double? SubSos { get; set; }

    public double? CombinedSos { get; set; }

    public decimal? PowerRating { get; set; }

    public virtual Team Team { get; set; } = null!;
}
