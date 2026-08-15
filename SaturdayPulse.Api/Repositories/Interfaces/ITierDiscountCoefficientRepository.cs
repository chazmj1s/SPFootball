using SaturdayPulse.Models;

namespace SaturdayPulse.Repositories.Interfaces
{
    /// <summary>
    /// Data access for TierDiscountCoefficients — append-only, potentially more than
    /// one row per season (see TierDiscountCoefficient remarks for why history is
    /// kept rather than overwritten).
    /// </summary>
    public interface ITierDiscountCoefficientRepository
    {
        /// <summary>
        /// Inserts a new coefficient row. Does not call SaveChangesAsync — caller is
        /// responsible, matching the rest of this codebase's repository convention.
        /// </summary>
        Task AddAsync(TierDiscountCoefficient coefficient, CancellationToken token = default);

        /// <summary>Returns every row computed for this season, oldest first.</summary>
        Task<List<TierDiscountCoefficient>> GetBySeasonAsync(int season, CancellationToken token = default);

        /// <summary>
        /// Returns the most recently computed row for this exact season (by
        /// ComputedAt), or null if the season has never been computed. This is the
        /// method a live consumer (e.g. BuildProjection) should use.
        /// </summary>
        Task<TierDiscountCoefficient?> GetLatestBySeasonAsync(int season, CancellationToken token = default);
    }
}
