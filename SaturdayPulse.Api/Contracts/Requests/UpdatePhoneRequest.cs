namespace SaturdayPulse.Contracts.Requests
{
    public class UpdatePhoneRequest
    {
        // Any reasonable format is fine — "(512) 555-1234", "512-555-1234",
        // "5125551234", or already-E.164 "+15125551234". Normalized server-side
        // in UserProfileService before storage. Null clears the phone number.
        public string? PhoneNumber { get; set; }
        public bool? MarketingSmsConsent { get; set; } // if provided, updates consent + source in the same call
    }
}
