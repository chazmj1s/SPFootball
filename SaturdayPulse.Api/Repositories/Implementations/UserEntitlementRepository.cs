using Microsoft.EntityFrameworkCore;
using SaturdayPulse.Data;
using SaturdayPulse.Models;
using SaturdayPulse.Repositories.Interfaces;

namespace SaturdayPulse.Repositories.Implementations
{
    public class UserEntitlementRepository : IUserEntitlementRepository
    {
        // Scoped to a single product for now — see UserEntitlement's class summary.
        // Selection-by-product is a repository concern, not a model concern;
        // revisit (probably a GetActiveAsync(userId, productKey) overload) once a
        // second product actually exists.
        private const string CfbSeasonPassKey = "cfb-season-pass";

        private readonly NCAAContext _context;
        public UserEntitlementRepository(NCAAContext context) => _context = context;

        public Task<List<UserEntitlement>> GetByUserIdAsync(string userId, CancellationToken token = default)
            => _context.UserEntitlements.Where(e => e.UserId == userId).ToListAsync(token);

        /// <summary>
        /// Matches ProductKey by prefix, not exact equality (added 2026-07-26).
        /// Real grants now store a season-specific ProductKey (e.g.
        /// "cfb-season-pass-2026", matching Apple/Google IAP's per-season SKU
        /// convention), so an exact match against the bare "cfb-season-pass"
        /// constant would never find them. StartsWith still matches the bare
        /// key too (the admin dev-toggle's non-seasonal grants), so both old
        /// and new rows resolve correctly with no data migration needed.
        /// </summary>
        public Task<UserEntitlement?> GetActiveCfbSeasonPassAsync(string userId, CancellationToken token = default)
            => _context.UserEntitlements.FirstOrDefaultAsync(e =>
                e.UserId == userId &&
                e.ProductKey.StartsWith(CfbSeasonPassKey) &&
                e.ExpiryDate.HasValue &&
                e.ExpiryDate.Value > DateTime.UtcNow,
                token);

        public async Task AddAsync(UserEntitlement entitlement, CancellationToken token = default)
        {
            entitlement.CreatedAt = DateTime.UtcNow;
            await _context.UserEntitlements.AddAsync(entitlement, token);
        }
    }
}
