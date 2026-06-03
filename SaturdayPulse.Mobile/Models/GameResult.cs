using Syncfusion.Licensing;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SaturdayPulse.Models
{
    [Preserve(AllMembers = true)]
    public class GameResult : INotifyPropertyChanged
    {
        public int     Id   { get; set; }
        public int     Year { get; set; }
        public int     Week { get; set; }

        public string? GameDate    { get; set; }
        public string? GameDay     { get; set; }
        public string  SeasonType   { get; set; } = "regular";

        /// <summary>Sequential position assigned by the ViewModel after load — used for "original order" sort.</summary>
        public int  SequenceNumber { get; set; }
        public bool IsOddRow => SequenceNumber % 2 == 1;

        // ── Home / Away identity ──────────────────────────────────────────

        public string  HomeName      { get; set; } = string.Empty;
        public int     HomeId        { get; set; }
        public string  HomeConf      { get; set; } = string.Empty;
        public string  HomeTier      { get; set; } = string.Empty;
        public int     HomePoints    { get; set; }
        public double? HomeProjScore { get; set; }

        public string  AwayName      { get; set; } = string.Empty;
        public int     AwayId        { get; set; }
        public string  AwayConf      { get; set; } = string.Empty;
        public string  AwayTier      { get; set; } = string.Empty;
        public int     AwayPoints    { get; set; }
        public double? AwayProjScore { get; set; }

        public char    Location  { get; set; }   // 'H' = has home team, 'N' = neutral
        public bool    IsPlayed  { get; set; }
        public int     ActualOU  { get; set; }
        public double? ProjOU    { get; set; }

        // ── Derived: who won ──────────────────────────────────────────────

        public bool HomeIsWinner => IsPlayed && HomePoints >= AwayPoints;
        public bool NeutralSite  => Location == 'N';

        // ── Display: visitor (away) on top, home on bottom ────────────────

        public string VisitorName  => AwayName;
        public string VisitorNameWithConf => string.IsNullOrEmpty(AwayConf)  ? AwayName  : $"{AwayName} ({AwayConf})";
        public string HomeNameWithConf    => string.IsNullOrEmpty(HomeConf)  ? HomeName  : $"{HomeName} ({HomeConf})";
        public string VisitorScore => IsPlayed ? AwayPoints.ToString() : "–";
        public string HomeScore    => IsPlayed ? HomePoints.ToString()  : "–";

        public bool HasProjection => HomeProjScore.HasValue && AwayProjScore.HasValue;

        public string ProjVisitorScore => AwayProjScore.HasValue
            ? $"{(int)Math.Round(AwayProjScore.Value)}" : "–";
        public string ProjHomeScore => HomeProjScore.HasValue
            ? $"{(int)Math.Round(HomeProjScore.Value)}" : "–";

        public string DisplayVisitorScore => IsPlayed
            ? $"{VisitorScore} ({ProjVisitorScore})"
            : $"({ProjVisitorScore})";
        public string DisplayHomeScore => IsPlayed
            ? $"{HomeScore} ({ProjHomeScore})"
            : $"({ProjHomeScore})";

        public int    ActualMargin      => AwayPoints - HomePoints;
        public string DisplayProjMargin => HasProjection
            ? $"{Math.Round((AwayProjScore!.Value - HomeProjScore!.Value) * 2, MidpointRounding.AwayFromZero) / 2:F1}" : "–";
        public string DisplayProjOU     => ProjOU.HasValue
            ? $"{(int)Math.Round(ProjOU.Value * 2, MidpointRounding.AwayFromZero) / 2:F1}" : "–";

        public string DisplayMargin => IsPlayed
            ? $"Spread: {ActualMargin} ({DisplayProjMargin})"
            : $"Spread: ({DisplayProjMargin})";
        public string DisplayOU => IsPlayed
            ? $"O/U: {ActualOU} ({DisplayProjOU})"
            : $"O/U: ({DisplayProjOU})";

        public string NeutralIndicator => NeutralSite ? " (N)" : string.Empty;

        // ── Group header ──────────────────────────────────────────────────

        public string GroupHeader
        {
            get
            {
                if (string.IsNullOrEmpty(GameDay) || string.IsNullOrEmpty(GameDate))
                    return $"Week {Week}";

                var parts    = GameDate.Split(' ');
                var monthDay = parts.Length >= 2 ? $"{parts[0]} {parts[1]}" : GameDate;
                return $"{GameDay}, {monthDay}";
            }
        }

        private bool _showGroupHeader;
        public bool ShowGroupHeader
        {
            get => _showGroupHeader;
            set { _showGroupHeader = value; OnPropertyChanged(); }
        }

        // ── Follow state ──────────────────────────────────────────────────

        private bool _homeIsFollowed;
        public bool HomeIsFollowed
        {
            get => _homeIsFollowed;
            set { _homeIsFollowed = value; OnPropertyChanged(); }
        }

        private bool _visitorIsFollowed;
        public bool VisitorIsFollowed
        {
            get => _visitorIsFollowed;
            set { _visitorIsFollowed = value; OnPropertyChanged(); }
        }

        private bool _isGameFavorited;
        public bool IsGameFavorited
        {
            get => _isGameFavorited;
            set { _isGameFavorited = value; OnPropertyChanged(); }
        }
        
        // ── Game detail expand ────────────────────────────────────────────

        private bool _isDetailsExpanded;
        public bool IsDetailsExpanded
        {
            get => _isDetailsExpanded;
            set { _isDetailsExpanded = value; OnPropertyChanged(); OnPropertyChanged(nameof(DetailsExpandIcon)); }
        }

        public string DetailsExpandIcon => _isDetailsExpanded ? "▲" : "▼";

        private GameTeamStats? _homeStats;
        public GameTeamStats? HomeStats
        {
            get => _homeStats;
            set { _homeStats = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasStats)); }
        }

        private GameTeamStats? _awayStats;
        public GameTeamStats? AwayStats
        {
            get => _awayStats;
            set { _awayStats = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasStats)); }
        }

        private GameLines? _lines;
        public GameLines? VegasLines
        {
            get => _lines;
            set { _lines = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasStats)); }
        }

        public bool HasStats => HomeStats != null && AwayStats != null;

        // ── Inter-division / detail visibility ────────────────────────────

        /// <summary>True when only one team has stats — FBS vs FCS matchup.</summary>
        public bool IsInterDivision =>
            (HomeStats == null) != (AwayStats == null);

        /// <summary>Show the Details toggle if we have stats OR it's inter-division.</summary>
        public bool ShowDetails => HasStats || IsInterDivision;

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
