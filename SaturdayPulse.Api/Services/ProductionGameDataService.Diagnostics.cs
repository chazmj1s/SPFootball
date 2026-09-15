using Microsoft.Extensions.Caching.Memory;
using SaturdayPulse.Contracts;
using SaturdayPulse.Contracts.Requests;
using SaturdayPulse.Contracts.Responses;
using SaturdayPulse.Interfaces;
using SaturdayPulse.Models;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using SaturdayPulse.Utilities;
using SQLitePCL;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Timers;

namespace SaturdayPulse.Services
{
    /// <summary>
    /// ProductionGameDataService — Diagnostics.
    /// Read-only database/health introspection.
    /// </summary>
    public partial class ProductionGameDataService
    {

        // ── Diagnostics ──────────────────────────────────────────────────────────

        public async Task<DiagnosticInfo> GetDiagnosticAsync(CancellationToken token = default)
        {
            var allTeams    = await _uow.Teams.GetAllAsync(token);
            var yearRecords = await _uow.TeamRecords.GetSinceYearWithTeamsAsync(1960, token);
            var totalGames  = (await _uow.Games.GetPlayedGamesSinceYearAsync(1960, token)).Count;

            var totalTeams             = allTeams.Count;
            var totalRecords           = yearRecords.Count;
            var recordsWithPowerRating = yearRecords.Count(tr => tr.PowerRating.HasValue);

            var years = yearRecords
                .Where(tr => tr.PowerRating.HasValue)
                .Select(tr => tr.Year)
                .Distinct()
                .OrderBy(y => y)
                .ToList();

            var yearStats = years.Select(y => (object)new
            {
                year              = y,
                teamsWithRankings = yearRecords.Count(tr => tr.Year == y && tr.PowerRating.HasValue)
            }).ToList();

            return new DiagnosticInfo("Connected", totalTeams, totalGames, totalRecords,
                recordsWithPowerRating, years.Select(y => (object)y).ToList(), yearStats);
        }
    }
}
