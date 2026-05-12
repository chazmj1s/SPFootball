using Syncfusion.Licensing;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NCAA_Power_Ratings.Mobile.Models
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

        public string TeamName       { get; set; }
        public string Conference     { get; set; }
        public string Division       { get; set; }
        public int    ActualWins     { get; set; }
        public int    ActualLosses   { get; set; }
        public int    ProjectedWins  { get; set; }
        public int    ProjectedLosses { get; set; }
        public double ProjectedWinPct { get; set; }
        public int    ProjectedFinish { get; set; }  // rank within conference
        public List<ProjectedGame> Games { get; set; } = new();

        // Expand/collapse game detail
        public bool IsExpanded
        {
            get => _isExpanded;
            set { _isExpanded = value; OnPropertyChanged(); OnPropertyChanged(nameof(ExpandIcon)); }
        }

        public string ExpandIcon => IsExpanded ? "▲" : "▼";

        // Display helpers
        public string ActualRecord     => $"{ActualWins}-{ActualLosses}";
        public string ProjectedRecord  => $"{ProjectedWins}-{ProjectedLosses}";

        public string RecordDisplay => ActualWins == ProjectedWins && ActualLosses == ProjectedLosses
            ? ActualRecord                                         // season complete
            : $"{ActualRecord}  →  {ProjectedRecord}";            // mid-season

        public string FinishDisplay => ProjectedFinish > 0 ? $"#{ProjectedFinish}" : "";

        // Color hint for finish — top 2 qualify
        public bool IsQualifier => ProjectedFinish <= 2;
        public bool IsBubble    => ProjectedFinish == 3;

        // Count of remaining projected games
        public int ProjectedGamesRemaining => Games.Count(g => g.IsProjected);

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
        public string Division         { get; set; }

        public string ConferenceRecord => $"{ConferenceWins}-{ConferenceLosses}";
        public string OverallRecord    => $"{OverallWins}-{OverallLosses}";
    }

    public class ChampionshipContender
    {
        public string TeamName { get; set; }
        public int ConferenceWins { get; set; }
        public int ConferenceLosses { get; set; }
        public string ConferenceRecord => $"{ConferenceWins}-{ConferenceLosses}";
    }

    // ── Championship matchup ──────────────────────────────────────────────

    public class ChampionshipMatchup : INotifyPropertyChanged
    {
        private bool _isExpanded;

        public string               Conference       { get; set; }
        public string               Format           { get; set; }
        public ChampionshipQualifier Qualifier1      { get; set; }
        public ChampionshipQualifier Qualifier2      { get; set; }
        public string               Qualifier1Method { get; set; }
        public string               Qualifier2Method { get; set; }
        public List<string>         TiebreakerLog    { get; set; } = new();
        public List<string>         StubsApplied     { get; set; } = new();
        public string               SimulatedThrough { get; set; }
        public List<ChampionshipContender> Contenders { get; set; } = new();

        // Tiebreaker used indicator
        public bool HasTiebreaker => TiebreakerLog.Any(l => l.Contains("TB"));
        public bool HasStubs      => StubsApplied.Any();

        // Expand tiebreaker log
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
            set { _isContendersExpanded = value; OnPropertyChanged(); OnPropertyChanged(nameof(ContendersExpandIcon)); }
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
