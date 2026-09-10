namespace SaturdayPulse.Services
{
    /// <summary>
    /// Tracks GameScorePollingService's live status (last tick, last CFBD
    /// call outcome, why the last tick skipped) plus process start time, for
    /// the mobile Debug Log health tiles (see LogsController.GetHealth).
    /// Written by GameScorePollingService, read by LogsController — same
    /// singleton pattern as ServerLogService, registered the same way in
    /// Program.cs. Not persisted — resets on app restart/redeploy, same
    /// tradeoff ServerLogService already accepts.
    ///
    /// CFBD connectivity is deliberately last-known-status, not a live probe
    /// on every health-check request — GameScorePollingService only calls
    /// CFBD inside today's kickoff window (see its remarks), so firing an
    /// extra CFBD call just to answer a health check would add load for no
    /// real benefit outside that window. CfbdLastCallUtc/Succeeded being
    /// null simply means "no poll attempt yet today" — the mobile tile
    /// should render that as a neutral/unknown state, not a failure.
    /// </summary>
    public class PollingStatusService
    {
        private readonly object _lock = new();

        /// <summary>
        /// Set once, at construction. Program.cs resolves this singleton
        /// eagerly right after the app is built, so this reflects true
        /// process start — not the timestamp of the first request that
        /// happens to touch it.
        /// </summary>
        public DateTime StartedAtUtc { get; } = DateTime.UtcNow;

        public DateTime? LastTickUtc { get; private set; }
        public string? LastSkipReason { get; private set; }
        public DateTime? LastCfbdCallUtc { get; private set; }
        public bool? LastCfbdCallSucceeded { get; private set; }
        public DateTime? LastErrorUtc { get; private set; }
        public string? LastErrorMessage { get; private set; }

        /// <summary>Call at the start of every PeriodicTimer tick, before the
        /// in-window check — clears the previous tick's skip reason and
        /// error so neither lingers past a tick that succeeded cleanly.</summary>
        public void RecordTick()
        {
            lock (_lock)
            {
                LastTickUtc = DateTime.UtcNow;
                LastSkipReason = null;
                LastErrorUtc = null;
                LastErrorMessage = null;
            }
        }

        public void RecordSkip(string reason)
        {
            lock (_lock) { LastSkipReason = reason; }
        }

        public void RecordCfbdCall(bool succeeded)
        {
            lock (_lock)
            {
                LastCfbdCallUtc = DateTime.UtcNow;
                LastCfbdCallSucceeded = succeeded;
            }
        }

        public void RecordError(Exception ex)
        {
            lock (_lock)
            {
                LastErrorUtc = DateTime.UtcNow;
                LastErrorMessage = ex.Message;
            }
        }
    }
}
