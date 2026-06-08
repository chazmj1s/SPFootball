using Microsoft.EntityFrameworkCore;
using SaturdayPulse.Data;
using SaturdayPulse.Models;
using SaturdayPulse.Repositories.Interfaces;

namespace SaturdayPulse.Repositories.Implementations
{
    public class ConferenceRepository(NCAAContext context) : IConferenceRepository
    {
        private readonly NCAAContext _context = context;

        public Task<List<Conference>> GetAllAsync(CancellationToken token = default)
            => _context.Conferences.OrderBy(c => c.Name).ToListAsync(token);

        public Task<Conference?> GetByConferenceIdAsync(int conferenceId, CancellationToken token = default)
            => _context.Conferences.FirstOrDefaultAsync(c => c.ConferenceId == conferenceId, token);

        public Task<Dictionary<int, Conference>> GetDictionaryAsync(CancellationToken token = default)
            => _context.Conferences.ToDictionaryAsync(c => c.ConferenceId, token);

        public async Task UpsertAsync(Conference conference, CancellationToken token = default)
        {
            var existing = await _context.Conferences
                .FirstOrDefaultAsync(c => c.ConferenceId == conference.ConferenceId, token);

            if (existing == null)
                _context.Conferences.Add(conference);
            else
            {
                existing.Name           = conference.Name;
                existing.ShortName      = conference.ShortName;
                existing.Abbreviation   = conference.Abbreviation;
                existing.Classification = conference.Classification;
            }
        }

        public async Task UpsertRangeAsync(IEnumerable<Conference> conferences, CancellationToken token = default)
        {
            var incoming    = conferences.ToList();
            var incomingIds = incoming.Select(c => c.ConferenceId).ToHashSet();

            var existing = await _context.Conferences
                .Where(c => incomingIds.Contains(c.ConferenceId))
                .ToDictionaryAsync(c => c.ConferenceId, token);

            foreach (var conference in incoming)
            {
                if (existing.TryGetValue(conference.ConferenceId, out var dbConf))
                {
                    dbConf.Name           = conference.Name;
                    dbConf.ShortName      = conference.ShortName;
                    dbConf.Abbreviation   = conference.Abbreviation;
                    dbConf.Classification = conference.Classification;
                }
                else
                    _context.Conferences.Add(conference);
            }
        }
    }
}
