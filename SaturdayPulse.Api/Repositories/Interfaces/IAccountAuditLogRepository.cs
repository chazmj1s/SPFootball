using SaturdayPulse.Models;

namespace SaturdayPulse.Repositories.Interfaces
{
    public interface IAccountAuditLogRepository
    {
        Task AddAsync(AccountAuditLog entry, CancellationToken token = default);
    }
}
