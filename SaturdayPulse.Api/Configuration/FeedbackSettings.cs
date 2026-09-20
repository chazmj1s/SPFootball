namespace SaturdayPulse.Configuration
{
    /// <summary>
    /// Bound from the "Feedback" configuration section. On the server set the
    /// environment variable Feedback__DiscordWebhookUrl (double underscore,
    /// same convention as RevenueCat__WebhookAuthToken) in docker-compose.
    /// The URL is a secret: it must never be committed to source control or
    /// compiled into the mobile app.
    /// </summary>
    public class FeedbackSettings
    {
        public string DiscordWebhookUrl { get; set; } = string.Empty;
    }
}
