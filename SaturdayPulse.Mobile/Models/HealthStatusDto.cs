namespace SaturdayPulse.Models
{
    /// <summary>
    /// Maps 1:1 onto LogsController.GetHealth's response — backs the mini
    /// admin health dashboard in Settings' Debug Log section (uptime,
    /// polling status, CFBD connectivity, DB connectivity tiles). Field
    /// names match the controller's anonymous object exactly.
    /// </summary>
    public class HealthStatusDto
    {
        public long      UptimeSeconds         { get; set; }
        public DateTime  ServerTimeUtc         { get; set; }
        public DateTime? PollingLastTickUtc    { get; set; }
        public string?   PollingLastSkipReason { get; set; }
        public DateTime? PollingLastErrorUtc   { get; set; }
        public DateTime? CfbdLastCallUtc       { get; set; }
        public bool?     CfbdLastCallSucceeded { get; set; }
        public bool      DbConnected           { get; set; }
        public DateTime  DbCheckedUtc          { get; set; }
    }
}
