using System.Text.Json.Serialization;

namespace SaturdayPulse.Contracts.Requests
{
    /// <summary>
    /// Subset of the RevenueCat webhook payload this API actually reads.
    /// Unknown fields are ignored by System.Text.Json.
    /// </summary>
    public class RevenueCatWebhookRequest
    {
        [JsonPropertyName("api_version")]
        public string? ApiVersion { get; set; }

        [JsonPropertyName("event")]
        public RevenueCatWebhookEvent? Event { get; set; }
    }

    public class RevenueCatWebhookEvent
    {
        /// <summary>TEST, INITIAL_PURCHASE, RENEWAL, CANCELLATION, EXPIRATION, ...</summary>
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("id")]
        public string? Id { get; set; }

        /// <summary>The App User ID the client passed to RevenueCat — our UserProfile.UserId (JWT sub).</summary>
        [JsonPropertyName("app_user_id")]
        public string? AppUserId { get; set; }

        [JsonPropertyName("original_app_user_id")]
        public string? OriginalAppUserId { get; set; }

        /// <summary>Apple: "cfb_season_pass". Google: "cfb_season_pass:{basePlanId}".</summary>
        [JsonPropertyName("product_id")]
        public string? ProductId { get; set; }

        [JsonPropertyName("purchased_at_ms")]
        public long? PurchasedAtMs { get; set; }

        [JsonPropertyName("expiration_at_ms")]
        public long? ExpirationAtMs { get; set; }

        /// <summary>APP_STORE, MAC_APP_STORE, PLAY_STORE, TEST_STORE, ...</summary>
        [JsonPropertyName("store")]
        public string? Store { get; set; }

        /// <summary>SANDBOX or PRODUCTION.</summary>
        [JsonPropertyName("environment")]
        public string? Environment { get; set; }

        /// <summary>CANCELLATION only: UNSUBSCRIBE, BILLING_ERROR, DEVELOPER_INITIATED, PRICE_INCREASE, CUSTOMER_SUPPORT (refund), UNKNOWN.</summary>
        [JsonPropertyName("cancel_reason")]
        public string? CancelReason { get; set; }

        [JsonPropertyName("transaction_id")]
        public string? TransactionId { get; set; }
    }
}
