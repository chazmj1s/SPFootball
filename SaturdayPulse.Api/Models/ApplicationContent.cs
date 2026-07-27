namespace SaturdayPulse.Models
{
    /// <summary>
    /// Single-row table holding all admin-editable, user-facing app content
    /// (About, Privacy Policy, Terms of Service, Season Pass, FAQ, Announcements,
    /// Release Notes) as one serialized JSON document. Deliberately simple —
    /// one table, one row, one blob — per the J1S Content Management design:
    /// single administrator, relatively little editable content, no need for a
    /// traditional per-field CMS schema.
    ///
    /// Exactly one row is expected to exist. Version increments on every save so
    /// the mobile app can cheaply detect staleness without comparing content.
    /// </summary>
    public class ApplicationContent
    {
        public int Id { get; set; }

        public int Version { get; set; }

        public DateTime LastModifiedUtc { get; set; }

        /// <summary>Serialized ApplicationContentDocument. See that type for shape.</summary>
        public string ContentJson { get; set; } = string.Empty;
    }
}
