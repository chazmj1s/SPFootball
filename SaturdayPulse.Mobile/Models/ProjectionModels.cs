using Syncfusion.Licensing;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SaturdayPulse.Models
{
    // ── Projected game detail ─────────────────────────────────────────────

    [Preserve(AllMembers = true)]
    public class ProjectedGame
    {
        public int    Week        { get; set; }
        public string Opponent    { get; set; }
        public string Location    { get; set; }  // "vs" or "@"
        public string Result      { get; set; }  // "W" or "L"
        public string Score       { get; set; }  // "24-21" — actual only
        public string ProjScore   { get; set; }  // "28-17" — projected only
        public string Confidence  { get; set; }  // "High" | "Medium" | "Low" | "Very Low"
        public string Type        { get; set; }  // "Actual" | "Projected"
        public bool   NeutralSite { get; set; }

        // Display helpers
        public bool IsActual    => Type == "Actual";
        public bool IsProjected => Type == "Projected";

        public string DisplayScore => IsActual
            ? Score
            : ProjScore != null ? $"({ProjScore})" : "–";

        public string DisplayResult => IsActual
            ? Result
            : $"({Result})";

        public string OpponentDisplay => NeutralSite
            ? $"{Opponent} (N)"
            : $"{Location} {Opponent}";
    }

    // ── Projected team standing ───────────────────────────────────────────

    public class ProjectedTeamStanding : INotifyPropertyChanged
    {
        private bool _isExpanded;
        private bool _isChartExpanded;
        private bool _chartDataLoaded;

        public string TeamName        { get; set; }
        public string Conference      { get; set; }
        public string Division        { get; set; }
        public int    ActualWins      { get; set; }
        public int    ActualLosses    { get; set; }
        public int    ProjectedWins   { get; set; }
        public int    ProjectedLosses { get; set; }
        public double ProjectedWinPct { get; set; }
        public int    ProjectedFinish { get; set; }  // rank within conference

        // Overall record — populated by the server alongside conference record
        public int OverallActualWins      { get; set; }
        public int OverallActualLosses    { get; set; }
        public int OverallProjectedWins   { get; set; }
        public int OverallProjectedLosses { get; set; }

        public bool IsOddRow { get; set; }

        public List<ProjectedGame> Games { get; set; } = new();

        // ── Expand/collapse game detail ───────────────────────────────────

        public bool IsExpanded
        {
            get => _isExpanded;
            set { _isExpanded = value; OnPropertyChanged(); OnPropertyChanged(nameof(ExpandIcon)); }
        }

        public string ExpandIcon => IsExpanded ? "▲" : "▼";

        // ── Expand/collapse win trajectory chart ──────────────────────────

        public bool IsChartExpanded
        {
            get => _isChartExpanded;
            set
            {
                _isChartExpanded = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ChartExpandIcon));
                if (_isChartExpanded) EnsureChartDataLoaded();
            }
        }

        public string ChartExpandIcon => IsChartExpanded ? "▲" : "▼";

        // ── Display helpers ───────────────────────────────────────────────

        // Conference record (existing)
        public string ActualConferenceRecord    => $"{ActualWins}-{ActualLosses}";
        public string ProjectedRecord => $"{ProjectedWins}-{ProjectedLosses}";

        // Overall record
        public string OverallActualRecord    => $"{OverallActualWins}-{OverallActualLosses}";
        public string OverallProjectedRecord => $"{OverallProjectedWins}-{OverallProjectedLosses}";

        // True if overall and conference records differ (i.e. not a conf-only display)
        public bool HasOverallRecord =>
            OverallActualWins > 0 || OverallActualLosses > 0 ||
            OverallProjectedWins > 0 || OverallProjectedLosses > 0;

        public string RecordDisplay => ActualWins == ProjectedWins && ActualLosses == ProjectedLosses
            ? ActualConferenceRecord
            : $"{ActualConferenceRecord}  →  {ProjectedRecord}";

        public string FinishDisplay   => ProjectedFinish > 0 ? $"#{ProjectedFinish}" : "";
        public bool   IsQualifier     => ProjectedFinish <= 2;
        public bool   IsBubble        => ProjectedFinish == 3;

        public int ProjectedGamesRemaining => Games.Count(g => g.IsProjected);

        // ── Win trajectory chart data ─────────────────────────────────────

        /// <summary>Cumulative actual wins by week.</summary>
        public ObservableCollection<ChartPoint> ActualWinPoints     { get; } = new();

        /// <summary>Cumulative projected wins by week (actual + projected games).</summary>
        public ObservableCollection<ChartPoint> ProjectedWinPoints  { get; } = new();

        // Dynamic Y axis — max projected wins + 1, min always 0
        public double WinChartYMax =>
            ProjectedWinPoints.Any()
                ? Math.Ceiling(ProjectedWinPoints.Max(p => p.Value) + 1)
                : 14;
        public double WinChartYInterval =>
            Math.Max(1, Math.Round(WinChartYMax / 4));

        private void EnsureChartDataLoaded()
        {
            if (_chartDataLoaded || Games == null || Games.Count == 0) return;
            LoadChartData();
            _chartDataLoaded = true;
        }

        private void LoadChartData()
        {
            ActualWinPoints.Clear();
            ProjectedWinPoints.Clear();

            int actualCumulative    = 0;
            int projectedCumulative = 0;

            foreach (var g in Games.OrderBy(g => g.Week))
            {
                if (g.IsActual)
                {
                    if (g.Result == "W") actualCumulative++;
                    // projected tracks actual until games are played
                    projectedCumulative = actualCumulative;
                    ActualWinPoints.Add(new ChartPoint
                        { Index = g.Week, Value = actualCumulative });
                    ProjectedWinPoints.Add(new ChartPoint
                        { Index = g.Week, Value = projectedCumulative });
                }
                else
                {
                    // Projected games: only add to ProjectedWinPoints
                    if (g.Result == "W") projectedCumulative++;
                    ProjectedWinPoints.Add(new ChartPoint
                        { Index = g.Week, Value = projectedCumulative });
                }
            }

            OnPropertyChanged(nameof(WinChartYMax));
            OnPropertyChanged(nameof(WinChartYInterval));
        }

        // ── INotifyPropertyChanged ────────────────────────────────────────

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // ── Championship qualifier ────────────────────────────────────────────

    public class ChampionshipQualifier
    {
        public string TeamName         { get; set; }
        public int    ConferenceWins   { get; set; }
        public int    ConferenceLosses { get; set; }
        public int    OverallWins      { get; set; }
        public int    OverallLosses    { get; set; }
        public int    ActualConferenceWins       { get; set; }
        public int    ActualConferenceLosses     { get; set; }
        public string Division         { get; set; }

        public string ConferenceRecord => $"{ConferenceWins}-{ConferenceLosses}";
        public string OverallRecord    => $"{OverallWins}-{OverallLosses}";
        public string ActualConferenceRecord     => $"{ActualConferenceWins}-{ActualConferenceLosses}";
    }

    public class ChampionshipContender
    {
        public string TeamName          { get; set; }
        public int    ConferenceWins    { get; set; }
        public int    ConferenceLosses  { get; set; }
        public int    OverallWins       { get; set; }
        public int    OverallLosses     { get; set; }
        public int    ActualConferenceWins        { get; set; }
        public int    ActualConferenceLosses      { get; set; }
        public string ConferenceRecord  => $"{ConferenceWins}-{ConferenceLosses}";
        public string OverallRecord     => $"{OverallWins}-{OverallLosses}";
        public string ActualConferenceRecord      => $"{ActualConferenceWins}-{ActualConferenceLosses}";
    }

    // ── Championship matchup ──────────────────────────────────────────────

    public class ChampionshipMatchup : INotifyPropertyChanged
    {
        private bool _isExpanded;

        public string                Conference       { get; set; }
        public string                Format           { get; set; }
        public ChampionshipQualifier Qualifier1       { get; set; }
        public ChampionshipQualifier Qualifier2       { get; set; }
        public string                Qualifier1Method { get; set; }
        public string                Qualifier2Method { get; set; }
        public List<string>          TiebreakerLog    { get; set; } = new();
        public List<string>          StubsApplied     { get; set; } = new();
        public string                SimulatedThrough { get; set; }
        public List<ChampionshipContender> Contenders { get; set; } = new();

        public bool HasTiebreaker => TiebreakerLog.Any(l => l.Contains("TB"));
        public bool HasStubs      => StubsApplied.Any();
        public bool HasContenders => Contenders.Any();

        public bool IsExpanded
        {
            get => _isExpanded;
            set { _isExpanded = value; OnPropertyChanged(); OnPropertyChanged(nameof(ExpandIcon)); }
        }

        public string ExpandIcon => IsExpanded ? "▲" : "▼";

        private bool _isContendersExpanded;
        public bool IsContendersExpanded
        {
            get => _isContendersExpanded;
            set
            {
                _isContendersExpanded = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ContendersExpandIcon));
            }
        }

        public string ContendersExpandIcon => IsContendersExpanded ? "▲" : "▼";

        public string TiebreakerSummary => HasTiebreaker
            ? "Tiebreaker applied"
            : "Outright qualifiers";

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
