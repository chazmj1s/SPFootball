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
    /// ProductionGameDataService — Predictions.
    /// Score prediction entry points: single matchup, batch, and Sandbox.
    /// </summary>
    public partial class ProductionGameDataService
    {

        // ── Predictions ──────────────────────────────────────────────────────────

        public Task<GamePrediction> PredictMatchupAsync(
            int year, string teamName, string opponentName, char location, int week,
            CancellationToken token = default)
            => _predictionService.PredictMatchup(year, teamName, opponentName, location, week, token);


        public Task<List<GamePrediction>> PredictMatchupsAsync(
            int year, List<MatchupRequest> matchups, CancellationToken token = default, int? asOfWeek = null)
            => _predictionService.PredictMatchups(
                year, asOfWeek ?? matchups.FirstOrDefault()?.Week ?? 0, matchups, token);


        public Task<GamePrediction> PredictSandboxMatchupAsync(
            string teamName, int teamYear,
            string opponentName, int opponentYear,
            CancellationToken token = default)
            => _predictionService.PredictSandboxMatchupAsync(
                teamName, teamYear, opponentName, opponentYear, token);
    }
}
