namespace SaturdayPulse.Services
{
    /// <summary>
    /// Computes UserEntitlement.ExpiryDate for CFB Season Pass purchases and
    /// renewals. NOT YET WIRED to any endpoint — there's no purchase/grant path
    /// built yet (admin-grant endpoint and Stripe are both still pending). The
    /// rule was worked out ahead of that so it's ready when the write path lands.
    /// Single-product only; extend the switch expressions if a second league's
    /// rule differs once that's real.
    /// </summary>
    public static class SeasonPassExpiryCalculator
    {
        private const string CfbSeasonPassKey = "cfb-season-pass";

        /// <summary>
        /// New purchase, or a renewal after the prior pass lapsed: computed fresh
        /// from the purchase date's season-year. Renewal while still active:
        /// extends the existing expiry by one season instead, so an early renewal
        /// doesn't waste the coverage the user already paid for. Strict "greater
        /// than" on currentExpiry — a renewal on the exact expiry date itself is
        /// treated as lapsed, matching UserEntitlement's own active-check.
        /// </summary>
        public static DateTime GetNextExpiry(string productKey, DateTime? currentExpiry, DateTime purchaseDate)
        {
            if (currentExpiry.HasValue && currentExpiry.Value > purchaseDate)
                return AddOneSeason(productKey, currentExpiry.Value);

            return GetFreshExpiry(productKey, purchaseDate);
        }

        /// <summary>
        /// Season-year uses the same convention as Games.Year/WeeklyRankings.Year —
        /// January games (postseason, CFP, championship) still belong to the PRIOR
        /// calendar year's season. A purchase in January 2027 buys into the 2026
        /// season, same as a September 2026 purchase would.
        /// </summary>
        private static DateTime GetFreshExpiry(string productKey, DateTime purchaseDate) => productKey switch
        {
            CfbSeasonPassKey => new DateTime(GetSeasonYear(purchaseDate) + 1, 7, 31),
            _ => throw new ArgumentException($"No expiry rule for product '{productKey}'")
        };

        private static int GetSeasonYear(DateTime purchaseDate) =>
            purchaseDate.Month == 1 ? purchaseDate.Year - 1 : purchaseDate.Year;

        // Month/day (7/31) is preserved by AddYears — the cutoff date never drifts.
        private static DateTime AddOneSeason(string productKey, DateTime currentExpiry) => productKey switch
        {
            CfbSeasonPassKey => currentExpiry.AddYears(1),
            _ => throw new ArgumentException($"No expiry rule for product '{productKey}'")
        };
    }
}
