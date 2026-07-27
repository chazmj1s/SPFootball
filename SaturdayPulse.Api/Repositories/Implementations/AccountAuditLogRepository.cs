using SaturdayPulse.Data;
using SaturdayPulse.Models;
using SaturdayPulse.Repositories.Interfaces;

namespace SaturdayPulse.Repositories.Implementations
{
    public class AccountAuditLogRepository : IAccountAuditLogRepository
    {
        private readonly NCAAContext _context;
        public AccountAuditLogRepository(NCAAContext context) => _context = context;

        public async Task AddAsync(AccountAuditLog entry, CancellationToken token = default)
            => await _context.AccountAuditLogs.AddAsync(entry, token);
    }
}
