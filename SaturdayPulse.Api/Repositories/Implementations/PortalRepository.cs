using Microsoft.EntityFrameworkCore;
using SaturdayPulse.Data;
using SaturdayPulse.Models;
using SaturdayPulse.Repositories.Interfaces;

namespace SaturdayPulse.Repositories.Implementations
{
    public class PortalRepository : IPortalRepository
    {
        private readonly NCAAContext _context;

        // Position tier weights — QB touches every play, trenches decide games.
        private static readonly Dictionary<string, double> PositionWeights = new(StringComparer.OrdinalIgnoreCase)
        {
            ["QB"]   = 2.5,
            ["OT"]   = 2.0,
            ["IOL"]  = 2.0,
            ["OL"]   = 2.0,
            ["DE"]   = 2.0,
            ["DL"]   = 2.0,
            ["EDGE"] = 2.0,
            ["CB"]   = 1.5,
            ["WR"]   = 1.5,
            ["LB"]   = 1.5,
            ["RB"]   = 1.5,
            ["S"]    = 1.0,
            ["TE"]   = 1.0,
            ["K"]    = 1.0,
            ["P"]    = 1.0,
            ["LS"]   = 1.0,
        };

        public PortalRepository(NCAAContext context) => _context = context;

        public Task<List<PortalEntry>> GetBySeasonAsync(int season, CancellationToken token = default)
            => _context.PortalEntries
                .Where(p => p.Season == season)
                .ToListAsync(token);

        public Task<List<int>> GetDistinctSeasonsAsync(CancellationToken token = default)
            => _context.PortalEntries
                .Select(p => p.Season)
                .Distinct()
                .OrderBy(s => s)
                .ToListAsync(token);

        public async Task UpsertSeasonAsync(
            int season, List<PortalEntry> entries, CancellationToken token = default)
        {
            var existing = await _context.PortalEntries
                .Where(p => p.Season == season)
                .ToListAsync(token);

            if (existing.Any())
                _context.PortalEntries.RemoveRange(existing);

            // Withdrawn already filtered at load time but double-check here.
            var toInsert = entries
                .Where(e => e.Eligibility != "Withdrawn")
                .ToList();

            await _context.PortalEntries.AddRangeAsync(toInsert, token);
        }

        /// <summary>
        /// Computes RosterStrength and PortalDelta for all FBS teams for the given season
        /// and persists them to TeamRecords.
        ///
        /// RosterStrength — absolute quality of incoming portal class (position-weighted avg stars),
        ///   normalized against league mean. Used in week 0 PowerRating adjustment.
        ///
        /// PortalDelta — net portal gain/loss (incoming minus outgoing, position-weighted),
        ///   normalized against league mean. Incorporated into Seed and Trend rolling averages.
        ///
        /// Only FBS teams are included — non-FBS origins/destinations are filtered out.
        /// Null for teams with no portal activity. Returns count of teams updated.
        /// </summary>
        public async Task<int> ComputePortalMetricsAsync(int season, CancellationToken token = default)
        {
            // Load FBS team names for filtering.
            var fbsTeams = await _context.Teams
                .Where(t => t.Division == "fbs" || t.Division == "FBS")
                .ToListAsync(token);

            var fbsNameToId = fbsTeams
                .ToDictionary(t => t.TeamName, t => t.TeamId, StringComparer.OrdinalIgnoreCase);

            // Load portal entries for the season.
            var entries = await _context.PortalEntries
                .Where(p => p.Season == season &&
                            p.Eligibility != "Withdrawn" &&
                            p.Stars > 0)
                .ToListAsync(token);

            // Filter to FBS-relevant entries only.
            var fbsEntries = entries
                .Where(e => fbsNameToId.ContainsKey(e.Destination ?? "") ||
                            fbsNameToId.ContainsKey(e.Origin ?? ""))
                .ToList();

            if (!fbsEntries.Any()) return 0;

            // Compute weighted scores per team.
            var incomingByTeam = new Dictionary<string, List<double>>(StringComparer.OrdinalIgnoreCase);
            var outgoingByTeam = new Dictionary<string, List<double>>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in fbsEntries)
            {
                var weight = PositionWeights.GetValueOrDefault(entry.Position ?? "", 1.0);
                var weightedStars = entry.Stars * weight;

                // Incoming — destination is FBS team.
                if (entry.Destination != null && fbsNameToId.ContainsKey(entry.Destination))
                {
                    if (!incomingByTeam.ContainsKey(entry.Destination))
                        incomingByTeam[entry.Destination] = new List<double>();
                    incomingByTeam[entry.Destination].Add((double)weightedStars);
                }

                // Outgoing — origin is FBS team and player landed somewhere.
                if (entry.Origin != null && fbsNameToId.ContainsKey(entry.Origin) &&
                    entry.Destination != null)
                {
                    if (!outgoingByTeam.ContainsKey(entry.Origin))
                        outgoingByTeam[entry.Origin] = new List<double>();
                    outgoingByTeam[entry.Origin].Add((double)weightedStars);
                }
            }

            // Compute league averages for normalization.
            var allTeamNames = incomingByTeam.Keys
                .Union(outgoingByTeam.Keys, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var rosterStrengthRaw = allTeamNames
                .Where(t => incomingByTeam.ContainsKey(t))
                .ToDictionary(
                    t => t,
                    t => incomingByTeam[t].Count > 0
                        ? incomingByTeam[t].Average()
                        : 0.0,
                    StringComparer.OrdinalIgnoreCase);

            var portalDeltaRaw = allTeamNames
                .ToDictionary(
                    t => t,
                    t =>
                    {
                        var inScore  = incomingByTeam.ContainsKey(t)  ? incomingByTeam[t].Sum()  : 0.0;
                        var outScore = outgoingByTeam.ContainsKey(t) ? outgoingByTeam[t].Sum() : 0.0;
                        return inScore - outScore;
                    },
                    StringComparer.OrdinalIgnoreCase);

            var leagueAvgRosterStrength = rosterStrengthRaw.Values.Any()
                ? rosterStrengthRaw.Values.Average() : 1.0;
            var leagueAvgPortalDelta    = portalDeltaRaw.Values.Any()
                ? portalDeltaRaw.Values.Average() : 0.0;
            var leagueStdDevDelta       = portalDeltaRaw.Values.Any()
                ? Math.Sqrt(portalDeltaRaw.Values.Average(v => Math.Pow(v - leagueAvgPortalDelta, 2)))
                : 1.0;

            // Normalize:
            // RosterStrength — ratio to league average (1.0 = average, >1.0 = above average)
            // PortalDelta    — Z-score vs league (0 = average, +1 = one std dev above)
            var rosterStrengthNorm = rosterStrengthRaw.ToDictionary(
                kvp => kvp.Key,
                kvp => leagueAvgRosterStrength > 0
                    ? Math.Round(kvp.Value / leagueAvgRosterStrength, 4)
                    : 0.0,
                StringComparer.OrdinalIgnoreCase);

            var portalDeltaNorm = portalDeltaRaw.ToDictionary(
                kvp => kvp.Key,
                kvp => leagueStdDevDelta > 0
                    ? Math.Round((kvp.Value - leagueAvgPortalDelta) / leagueStdDevDelta, 4)
                    : 0.0,
                StringComparer.OrdinalIgnoreCase);

            // Persist to TeamRecords.
            var teamRecords = await _context.TeamRecords
                .Where(tr => tr.Year == season)
                .ToListAsync(token);

            int updated = 0;
            foreach (var record in teamRecords)
            {
                var team = fbsTeams.FirstOrDefault(t => t.TeamId == record.TeamID);
                if (team == null) continue;

                var teamName = team.TeamName;

                record.RosterStrength = rosterStrengthNorm.TryGetValue(teamName, out var rs)
                    ? (decimal)rs : null;
                record.PortalDelta = portalDeltaNorm.TryGetValue(teamName, out var pd)
                    ? (decimal)pd : null;

                updated++;
            }

            await _context.SaveChangesAsync(token);

            return updated;
        }
    }
}
