namespace SaturdayPulse.Models
{
    /// <summary>
    /// Permanent audit trail keyed only on the Auth0 UserId - no profile
    /// information (no handle, email, phone) ever lives here, and there is
    /// deliberately NO foreign key relationship to UserProfile. That's the
    /// whole point: DeleteAccountAsync hard-deletes UserProfile and every
    /// related table, but these rows must survive that deletion intact, for
    /// compliance/audit purposes (proving when an account existed, what it
    /// was granted, and when it was deleted, without retaining any PII).
    ///
    /// EventType is a free string (not an enum), matching the same convention
    /// as UserEntitlement.Source - "AccountCreated" | "SeasonPassGranted" |
    /// "AccountDeleted" today, more values addable later with no migration.
    ///
    /// ProductKey/PassYear/Source are only populated for SeasonPassGranted
    /// rows - written for every entitlement grant (beta, manual-grant, and
    /// eventually stripe/IAP), not just real purchases, so the trail is
    /// complete rather than sparse until real payments exist. The mobile
    /// self-service dev-entitlement toggle deliberately does NOT write here -
    /// it's explicitly not a real grant.
    /// </summary>
    public class AccountAuditLog
    {
        public int Id { get; set; }

        public string UserId { get; set; } = null!;

        public string EventType { get; set; } = null!;

        public DateTime EventAt { get; set; }

        public string? ProductKey { get; set; }

        public int? PassYear { get; set; }

        public string? Source { get; set; }
    }
}
