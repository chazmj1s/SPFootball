using Microsoft.EntityFrameworkCore;
using SaturdayPulse.Data;
using SaturdayPulse.Models;
using SaturdayPulse.Repositories.Interfaces;

namespace SaturdayPulse.Repositories.Implementations
{
    public class ApplicationContentRepository : IApplicationContentRepository
    {
        private readonly NCAAContext _context;
        public ApplicationContentRepository(NCAAContext context) => _context = context;

        public Task<ApplicationContent?> GetAsync(CancellationToken token = default)
            => _context.ApplicationContent.FirstOrDefaultAsync(token);

        public async Task CreateAsync(ApplicationContent content, CancellationToken token = default)
            => await _context.ApplicationContent.AddAsync(content, token);

        public async Task UpdateAsync(int version, string contentJson, DateTime lastModifiedUtc, CancellationToken token = default)
        {
            var existing = await _context.ApplicationContent.FirstOrDefaultAsync(token);
            if (existing == null) return;

            existing.Version = version;
            existing.ContentJson = contentJson;
            existing.LastModifiedUtc = lastModifiedUtc;
        }
    }
}
