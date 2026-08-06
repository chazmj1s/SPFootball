using Microsoft.EntityFrameworkCore;
using System.Globalization;
using SaturdayPulse.Contracts;
using SaturdayPulse.Data;
using SaturdayPulse.Models;

namespace SaturdayPulse.Repositories
{
    /// <summary>
    /// EF Core repository for the Projections table (SQLite).
    /// Upsert uses INSERT OR REPLACE, which honours the UNIQUE constraint
    /// on (GameId, Year, Week) — existing rows are atomically replaced.
    /// </summary>
    public class ProjectionRepository : IProjectionRepository
    {
        private readonly NCAAContext _ctx;

        public ProjectionRepository(NCAAContext ctx) => _ctx = ctx;

        // ── Queries ───────────────────────────────────────────────────────────────

        public Task<List<Projection>> GetByYearAsync(int year, CancellationToken token = default)
            => _ctx.Projections
                   .Where(p => p.Year == year)
                   .ToListAsync(token);

        public Task<List<Projection>> GetByYearAndWeekAsync(int year, int week, CancellationToken token = default)
            => _ctx.Projections
                   .Where(p => p.Year == year && p.Week == week)
                   .ToListAsync(token);

        public Task<Projection> GetById(int gameId, CancellationToken token = default)
            => _ctx.Projections
                   .FirstOrDefaultAsync(p => p.GameId == gameId, token);        

        // ── Writes ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Bulk upsert via SQLite INSERT OR REPLACE.
        /// Batched in groups of 50 to stay under SQLite's default 999-parameter limit
        /// (each row uses 8 parameters → 50 × 8 = 400, comfortably within limit).
        /// </summary>
        public async Task UpsertManyAsync(IEnumerable<Projection> projections, CancellationToken token = default)
        {
            const int batchSize = 50;
            var batch = new List<Projection>(batchSize);

            foreach (var proj in projections)
            {
                batch.Add(proj);

                if (batch.Count == batchSize)
                {
                    await InsertOrReplaceBatchAsync(batch, token);
                    batch.Clear();
                }
            }

            if (batch.Count > 0)
                await InsertOrReplaceBatchAsync(batch, token);
        }

        // ── Private ───────────────────────────────────────────────────────────────

        private async Task InsertOrReplaceBatchAsync(
            List<Projection> batch, CancellationToken token)
        {
            var valueClauses = new List<string>(batch.Count);

            foreach (var p in batch)
            {
                // Decimals explicitly formatted with InvariantCulture — plain string
                // interpolation uses the current thread's culture, and a comma-decimal
                // locale would render e.g. PredictedSpread=2.5 as "2,5", breaking the
                // SQL (comma is the VALUES separator) rather than just being wrong.
                // Ints are culture-safe as-is.
                var spread = p.PredictedSpread.ToString(CultureInfo.InvariantCulture);
                var total = p.PredictedTotal.ToString(CultureInfo.InvariantCulture);
                var winProb = p.HomeWinProbability.ToString(CultureInfo.InvariantCulture);

                valueClauses.Add(
                    $"({p.GameId},{p.Year},{p.Week},{p.HomeTeamId},{p.AwayTeamId},{spread},{total},{winProb},{p.HomePoints},{p.AwayPoints})");
            }

            var sql = $@"
INSERT OR REPLACE INTO Projections
    (GameId, Year, Week, HomeTeamId, AwayTeamId,
     PredictedSpread, PredictedTotal, HomeWinProbability, HomePoints, AwayPoints)
VALUES
    {string.Join(",\n    ", valueClauses)};";

            await _ctx.Database.ExecuteSqlRawAsync(sql, token);
        }
    }
}
