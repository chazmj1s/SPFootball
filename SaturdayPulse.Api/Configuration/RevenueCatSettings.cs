namespace SaturdayPulse.Configuration
{
    /// <summary>
    /// Bound from the "RevenueCat" configuration section. In Azure set the
    /// App Service Application Setting RevenueCat__WebhookAuthToken (double
    /// underscore, same convention as Admin__ApiKey). The value must match,
    /// verbatim, the Authorization header value configured on the RevenueCat
    /// webhook (Project > Integrations > Webhooks).
    /// </summary>
    public class RevenueCatSettings
    {
        public string WebhookAuthToken { get; set; } = string.Empty;
    }
}
