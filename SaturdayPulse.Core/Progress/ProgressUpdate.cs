namespace SaturdayPulse.Core.Progress
{
    /// <summary>
    /// One unit of progress from a long-running backfill/bulk operation, streamed
    /// from Api to AdminBlazor as it happens rather than batched into one final
    /// result. Shared type — both sides deserialize/serialize the same record,
    /// same pattern as ApplicationContentDocument.
    /// </summary>
    /// <param name="Item">The unit just processed — usually a year ("1965"), sometimes
    /// a year+week ("1965 wk 3"), or a season for portal/roster ops.</param>
    /// <param name="Success">False if this item failed. The stream continues after a
    /// failed item — one bad year shouldn't silently abort a 60-year backfill.</param>
    /// <param name="Message">Human-readable detail for the log line — a count, an
    /// error message, or (for BuildTeamsConferenceHistory in dry-run mode) a
    /// description of what would have changed.</param>
    public record ProgressUpdate(string Item, bool Success, string Message);
}
