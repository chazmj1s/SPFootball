using System.Text.Json;

namespace SaturdayPulse.AdminBlazor.Services.Models;

public class OpParams
{
    public int? Year { get; set; }
    public int? Week { get; set; }
}

public enum OpParamType { Year, Week }

public class Operation
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public required double EstimateMinutes { get; init; }
    public bool Selected { get; set; }
    public bool AutoSelected { get; set; }
    public required List<string> Dependencies { get; init; }
    public required List<OpParamType> ParamTypes { get; init; }
    public required bool YearRequired { get; init; }
    public required bool WeekRequired { get; init; }
    public required OpParams Params { get; init; }
    public required Func<OpParams, Task<JsonElement>> Call { get; init; }
}

public class Tier
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public bool Collapsed { get; set; }
    public int? TierYear { get; set; }
    public required List<Operation> Ops { get; init; }
}
