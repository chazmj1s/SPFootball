using Microsoft.AspNetCore.Components;
using SaturdayPulse.AdminBlazor.Services;
using SaturdayPulse.Core.Progress;

namespace SaturdayPulse.AdminBlazor.Components.Pages
{
    public enum LogStatus { Info, Running, Success, Error }

    public record LogEntry(string Time, string Message, LogStatus Status);

    /// <summary>
    /// One operation on the console. Streaming ops (the long ones) call StreamCall
    /// and get a log line per yielded ProgressUpdate. Single-shot ops (Conferences,
    /// Score Differentials, Matchup Histories — none of these loop internally, so
    /// there's nothing to stream) call SingleCall once and log one line at the end.
    ///
    /// Consolidated per Charlie's direction: Team Records / SOS / Power Ratings /
    /// Rankings / Rolling Averages aren't listed — they're computed inside
    /// BackfillWeeklyRankings now. AssignPostseasonWeeksBulk and LoadPortalBulk
    /// live on the Data Ops page instead.
    /// </summary>
    public class RebuildOp
    {
        public required string Key { get; init; }
        public required string Label { get; init; }
        public bool Selected { get; set; }
        public bool RequiresYear { get; init; } = true;
        public int Year { get; set; }
        public double EstimateMinutes { get; init; }
        public bool HasDryRun { get; init; }
        public bool DryRun { get; set; }
        public Func<int, bool, CancellationToken, IAsyncEnumerable<ProgressUpdate>>? StreamCall { get; init; }
        public Func<int, CancellationToken, Task<string>>? SingleCall { get; init; }

        public string EstimateLabel => EstimateMinutes < 1 ? "<1 min" : $"{EstimateMinutes:0} min";
    }

    public class RebuildTier
    {
        public required string Label { get; init; }
        public required List<RebuildOp> Ops { get; init; }
        public bool Collapsed { get; set; }

        /// <summary>Null = "no override" (children keep whatever they're individually set to).
        /// Setting this cascades to every year-bearing op in the tier.</summary>
        public int? TierYear { get; set; }
    }

    public partial class MetricsRebuild : ComponentBase
    {
        [Inject] private AdminApiService Api { get; set; } = default!;

        protected readonly int CurrentYear = DateTime.Now.Year;
        protected List<int> Years { get; private set; } = [];

        protected bool Running { get; set; }
        protected List<LogEntry> Log { get; } = [];
        protected List<RebuildTier> Tiers { get; private set; } = [];

        protected override void OnInitialized()
        {
            Years = Enumerable.Range(1965, CurrentYear - 1964).Reverse().ToList();
            var y = CurrentYear;

            Tiers =
            [
                new RebuildTier
                {
                    Label = "Data Load",
                    Ops =
                    [
                        new RebuildOp
                        {
                            Key = "conferences", Label = "Load Conferences", RequiresYear = false, EstimateMinutes = 0.1,
                            SingleCall = async (_, ct) =>
                            {
                                var res = await Api.LoadConferencesAsync(ct);
                                return res.TryGetProperty("count", out var c) ? $"{c} conferences loaded" : "done";
                            }
                        },
                        new RebuildOp
                        {
                            Key = "teams", Label = "Load Teams (Bulk)", Year = y, EstimateMinutes = 0.5,
                            StreamCall = (year, _, ct) => Api.LoadTeamsBulkStreamAsync(year, ct)
                        },
                        new RebuildOp
                        {
                            Key = "games", Label = "Load Games (Bulk)", Year = y, EstimateMinutes = 3,
                            StreamCall = (year, _, ct) => Api.LoadGamesBulkStreamAsync(year, ct)
                        },
                        new RebuildOp
                        {
                            Key = "lines", Label = "Load Lines (Bulk)", Year = y, EstimateMinutes = 3,
                            StreamCall = (year, _, ct) => Api.LoadLinesBulkStreamAsync(year, ct)
                        },
                        new RebuildOp
                        {
                            // Dry-run defaults ON — see the doc comment on
                            // GameDataService.BuildTeamsConferenceHistoryStreamAsync.
                            // Flip off deliberately once CFBD's naming for the 2026
                            // Pac-12 reconstitution has been checked.
                            Key = "conferenceHistory", Label = "Update Conference History", Year = y, EstimateMinutes = 5,
                            HasDryRun = true, DryRun = true,
                            StreamCall = (year, dryRun, ct) => Api.BuildTeamsConferenceHistoryStreamAsync(year, dryRun, ct)
                        },
                    ]
                },
new RebuildTier
{
    Label = "Rankings",
    Ops =
    [
        new RebuildOp
        {
            // Real dependency is on Backfill Weekly Rankings below, not Initialize
            // Seasons — WeeklyRankingsService.ComputeAndSaveAsync's BuildProjection
            // step is the only consumer of TierDiscountCoefficients. InitializeSeason
            // doesn't build any Projection rows, so it has no dependency on this at
            // all (confirmed by reading the full method — no reference anywhere).
            // Placed first in the tier anyway, since it must precede Weekly Rankings
            // and doing so ahead of both is simplest.
            Key = "tierDiscount", Label = "Backfill Tier Discount Coefficients", Year = y, EstimateMinutes = 2,
            SingleCall = async (year, ct) =>
            {
                var res = await Api.ComputeTierDiscountCoefficientsBulkAsync(year, ct: ct);
                return res.TryGetProperty("message", out var m) ? m.GetString() ?? "done" : "done";
            }
        },
        new RebuildOp
        {
            Key = "initSeasons", Label = "Backfill Initialize Seasons", Year = y, EstimateMinutes = 5,
            StreamCall = (year, _, ct) => Api.BackfillInitializeSeasonsStreamAsync(year, ct)
        },
        new RebuildOp
        {
            Key = "weeklyRankings", Label = "Backfill Weekly Rankings", Year = y, EstimateMinutes = 40,
            StreamCall = (year, _, ct) => Api.BackfillWeeklyRankingsStreamAsync(year, ct)
        },
    ]
},                new RebuildTier
                {
                    Label = "Analytics",
                    Ops =
                    [
                        // "Backfill Projections" removed — Option C in
                        // WeeklyRankingsService.ComputeAndSaveAsync now writes
                        // Projections as a side effect of Backfill Weekly Rankings
                        // (Rankings tier, above). Running this separately would have
                        // written stale-shape rows into a table that now assumes at
                        // most one locked Projection row per game.
                        new RebuildOp
                        {
                            Key = "scoreDiffs", Label = "Score Differentials", Year = y, EstimateMinutes = 3,
                            SingleCall = async (year, ct) =>
                            {
                                var res = await Api.BuildAvgScoreDifferentialsAsync(year, ct);
                                return res.TryGetProperty("message", out var m) ? m.GetString() ?? "done" : "done";
                            }
                        },
                        new RebuildOp
                        {
                            Key = "matchups", Label = "Matchup Histories", RequiresYear = false, EstimateMinutes = 2,
                            SingleCall = async (_, ct) =>
                            {
                                var res = await Api.CalculateMatchupHistoriesAsync(ct);
                                return res.TryGetProperty("message", out var m) ? m.GetString() ?? "done" : "done";
                            }
                        },
                    ]
                },
            ];
        }

        protected IEnumerable<RebuildOp> AllOps => Tiers.SelectMany(t => t.Ops);
        protected List<RebuildOp> SelectedOps => AllOps.Where(o => o.Selected).ToList();
        protected double TotalEstimateMinutes => SelectedOps.Sum(o => o.EstimateMinutes);

        protected string EstimateLabel
        {
            get
            {
                var m = TotalEstimateMinutes;
                if (m == 0) return "";
                if (m < 60) return $"~{Math.Round(m)} min";
                var h = (int)(m / 60);
                var rem = (int)Math.Round(m % 60);
                return rem > 0 ? $"~{h}h {rem}m" : $"~{h}h";
            }
        }

        protected void SelectAll() { foreach (var o in AllOps) o.Selected = true; }
        protected void ClearAll() { foreach (var o in AllOps) o.Selected = false; }
        protected void ToggleTier(RebuildTier tier) => tier.Collapsed = !tier.Collapsed;

        protected bool IsTierChecked(RebuildTier tier) => tier.Ops.All(o => o.Selected);
        protected int TierSelectedCount(RebuildTier tier) => tier.Ops.Count(o => o.Selected);

        protected void OnTierToggle(RebuildTier tier, bool selected)
        {
            foreach (var op in tier.Ops) op.Selected = selected;
        }

        /// <summary>Tier-level year override — cascades to every year-bearing op in
        /// the tier, same as the old Angular console's tier header year select.</summary>
        protected void OnTierYearChanged(RebuildTier tier, int? year)
        {
            tier.TierYear = year;
            if (year is not null)
                foreach (var op in tier.Ops.Where(o => o.RequiresYear))
                    op.Year = year.Value;
        }

        /// <summary>If a child's year is changed individually and no longer matches
        /// the tier override, the tier header resets to "no override" — it doesn't
        /// fight the user's explicit per-op choice.</summary>
        protected void OnChildYearChanged(RebuildTier tier, RebuildOp op, int year)
        {
            op.Year = year;
            var allMatch = tier.Ops.Where(o => o.RequiresYear).All(o => o.Year == tier.TierYear);
            if (!allMatch) tier.TierYear = null;
        }

        private static string Timestamp() => DateTime.Now.ToString("HH:mm:ss");

        private void AppendLog(string message, LogStatus status)
        {
            Log.Add(new LogEntry(Timestamp(), message, status));
            StateHasChanged();
        }

        protected async Task RunSelectedAsync()
        {
            if (SelectedOps.Count == 0) return;

            Running = true;
            AppendLog($"── Rebuild  {EstimateLabel} ──", LogStatus.Info);

            foreach (var op in SelectedOps)
            {
                var yearStr = op.RequiresYear ? $" ({op.Year})" : "";
                AppendLog($"Starting {op.Label}{yearStr}{(op.HasDryRun && op.DryRun ? " [DRY RUN]" : "")}...", LogStatus.Running);

                try
                {
                    if (op.StreamCall is not null)
                    {
                        await foreach (var update in op.StreamCall(op.Year, op.DryRun, CancellationToken.None))
                        {
                            AppendLog(
                                $"{(update.Success ? "✓" : "✗")} {op.Label} — {update.Item}: {update.Message}",
                                update.Success ? LogStatus.Success : LogStatus.Error);
                        }
                    }
                    else if (op.SingleCall is not null)
                    {
                        var result = await op.SingleCall(op.Year, CancellationToken.None);
                        AppendLog($"✓ {op.Label} — {result}", LogStatus.Success);
                    }
                }
                catch (Exception ex)
                {
                    AppendLog($"✗ {op.Label} failed — {ex.Message}", LogStatus.Error);
                }
            }

            AppendLog("── Complete ──", LogStatus.Info);
            Running = false;
        }

        protected void ClearLog() => Log.Clear();
    }
}
