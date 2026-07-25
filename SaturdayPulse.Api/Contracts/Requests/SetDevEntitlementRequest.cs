namespace SaturdayPulse.Contracts.Requests
{
    /// <summary>
    /// Body for PATCH /api/user/me/dev-entitlement. Admin-only — see
    /// UserProfileService.SetDevEntitlementAsync for the server-side
    /// IsAdmin check (not just a client-side hidden button).
    /// </summary>
    public class SetDevEntitlementRequest
    {
        public bool Enabled { get; set; }
    }
}
