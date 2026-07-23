using SaturdayPulse.Models;

namespace SaturdayPulse.Repositories.Interfaces
{
    public interface IUserEntitlementRepository
    {
        /// <summary>All entitlement rows for a user, any product, any status.</summary>
        Task<List<UserEntitlement>> GetByUserIdAsync(string userId, CancellationToken token = default);

        /// <summary>
        /// The active CFB Season Pass entitlement for a user, or null if they
        /// don't have one (never purchased, or expired). Hardcoded to
        /// "cfb-season-pass" — see UserEntitlement's class summary for why.
        /// </summary>
        Task<UserEntitlement?> GetActiveCfbSeasonPassAsync(string userId, CancellationToken token = default);

        Task AddAsync(UserEntitlement entitlement, CancellationToken token = default);
    }
}
