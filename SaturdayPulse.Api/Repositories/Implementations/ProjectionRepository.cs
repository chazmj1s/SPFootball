using Microsoft.EntityFrameworkCore;
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

        // ── Writes ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Bulk upsert via SQLite INSERT OR REPLACE.
        /// Batched in groups of 50 to stay under SQLite's default 999-parameter limit
        /// (each row uses 8 parameters → 50 × 8 = 400, comfortably within limit).
        /// </summary>
        public async Task UpsertManyAsync(
            IEnumerable<Projection> projections, CancellationToken token = default)
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
            var parameters   = new List<object>(batch.Count * 8);
            int i            = 0;

            foreach (var p in batch)
            {
                valueClauses.Add(
                    $"({{{i}}},{{{i+1}}},{{{i+2}}},{{{i+3}}},{{{i+4}}},{{{i+5}}},{{{i+6}}},{{{i+7}}})");

                parameters.Add(p.GameId);
                parameters.Add(p.Year);
                parameters.Add(p.Week);
                parameters.Add(p.HomeTeamId);
                parameters.Add(p.AwayTeamId);
                parameters.Add(p.PredictedSpread);
                parameters.Add(p.PredictedTotal);
                parameters.Add(p.HomeWinProbability);

                i += 8;
            }

            var sql = $@"
INSERT OR REPLACE INTO Projections
    (GameId, Year, Week, HomeTeamId, AwayTeamId,
     PredictedSpread, PredictedTotal, HomeWinProbability)
VALUES
    {string.Join(",\n    ", valueClauses)};";

            await _ctx.Database.ExecuteSqlRawAsync(sql, parameters.ToArray(), token);
        }
    }
}
