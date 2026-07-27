namespace SaturdayPulse.Core.Content
{
    /// <summary>
    /// Wire/storage shape of ApplicationContent.ContentJson (Api-side entity).
    /// Shared between Api, AdminBlazor, and Mobile so all three deserialize
    /// the exact same shape - Api serializes it into the ContentJson column
    /// and returns it from GET/PUT /api/content, AdminBlazor edits it, and
    /// Mobile reads it read-only to render About/Privacy Policy/Terms of
    /// Service/etc. Living in SaturdayPulse.Core instead of being duplicated
    /// per-project is the whole point of this library existing.
    ///
    /// Adding a new content section (e.g. a future "Roadmap" page) means
    /// adding one property here - no migration required on the Api side,
    /// since the whole thing is stored as one JSON blob.
    /// </summary>
    public class ApplicationContentDocument
    {
        public int Version { get; set; }
        public ContentSection About { get; set; } = new();
        public ContentSection PrivacyPolicy { get; set; } = new();
        public ContentSection TermsOfService { get; set; } = new();
        public ContentSection SeasonPass { get; set; } = new();
        public ContentSection Faq { get; set; } = new();
        public ContentSection Announcements { get; set; } = new();
        public ContentSection ReleaseNotes { get; set; } = new();
    }

    public class ContentSection
    {
        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
    }
}
