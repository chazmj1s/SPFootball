using Syncfusion.Licensing;
using System.ComponentModel;
using System.Globalization;
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
        public string? GameDay { get; set; }
        public string? GameTime { get; set; }
        public string  SeasonType   { get; set; } = "regular";

        /// <summary>Sequential position assigned by the ViewModel after load — used for "original order" sort.</summary>
        public int  SequenceNumber { get; set; }
        public bool IsOddRow => SequenceNumber % 2 == 1;

        // ── Home / Away identity ──────────────────────────────────────────

        public string  HomeName      { get; set; } = string.Empty;
        public int     HomeId        { get; set; }
        public string  HomeConf      { get; set; } = string.Empty;
        public string  HomeTier      { get; set; } = string.Empty;

        // HomePoints/AwayPoints are full properties (not auto-properties) so that
        // GameDataApiService.RefreshGameAsync() updates propagate to the bound UI.
        // Prior to the manual-refresh feature these were plain `{ get; set; }` —
        // fine when only ever set once during initial mapping, but silent when
        // updated later on an already-bound instance. Both fire notifications for
        // every display/derived property that depends on the raw score.
        private int _homePoints;
        public int HomePoints
        {
            get => _homePoints;
            set
            {
                if (_homePoints == value) return;
                _homePoints = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HomeScore));
                OnPropertyChanged(nameof(DisplayHomeScore));
                OnPropertyChanged(nameof(ActualMargin));
                OnPropertyChanged(nameof(DisplayMargin));
                OnPropertyChanged(nameof(HomeIsWinner));
            }
        }

        public double? HomeProjScore { get; set; }

        public string  AwayName      { get; set; } = string.Empty;
        public int     AwayId        { get; set; }
        public string  AwayConf      { get; set; } = string.Empty;
        public string  AwayTier      { get; set; } = string.Empty;

        private int _awayPoints;
        public int AwayPoints
        {
            get => _awayPoints;
            set
            {
                if (_awayPoints == value) return;
                _awayPoints = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(VisitorScore));
                OnPropertyChanged(nameof(DisplayVisitorScore));
                OnPropertyChanged(nameof(ActualMargin));
                OnPropertyChanged(nameof(DisplayMargin));
                OnPropertyChanged(nameof(HomeIsWinner));
            }
        }

        public double? AwayProjScore { get; set; }

        public char    Location  { get; set; }   // 'H' = has home team, 'N' = neutral
        public bool    IsPlayed  { get; set; }
        public int     ActualOU  { get; set; }
        public double? ProjOU { get; set; }
        public double? ProjMargin { get; set; }

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

        public int    ActualMargin      => HomePoints - AwayPoints;
        public string DisplayProjMargin => ProjMargin.HasValue
            ? $"{Math.Round(ProjMargin.Value, 1)}" : "–";
        public string DisplayProjOU     => ProjOU.HasValue
            ? $"{Math.Round(ProjOU.Value, 1)}" : "–";

        public string DisplayMargin => IsPlayed
            ? $"Margin: {ActualMargin} ({DisplayProjMargin})"
            : $"Margin: ({DisplayProjMargin})";
        public string DisplayOU => IsPlayed
            ? $"O/U: {ActualOU} ({DisplayProjOU})"
            : $"O/U: ({DisplayProjOU})";

        public string NeutralIndicator => NeutralSite ? " (N)" : string.Empty;

        // ── Display: kickoff time ─────────────────────────────────────────
        // GameTime carries the raw "HH:mm:ss" (24-hour, invariant) format —
        // same as the API's Games.KickoffTime column. Deliberately NOT
        // pre-formatted upstream: a display string round-tripped back
        // through parsing for sort purposes (see GameDataCacheService.
        // ParseKickoff) is fragile if the format/culture used to produce it
        // ever drifts from the one used to parse it. Format only happens
        // here, with InvariantCulture pinned explicitly on both this parse
        // and the ToString below — no implicit CurrentCulture dependency.
        public string DisplayGameTime =>
            DateTime.TryParseExact(GameTime, "HH:mm:ss", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var t)
                ? t.ToString("h:mm tt", CultureInfo.InvariantCulture)
                : string.Empty;

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

        // ── Manual refresh state ────────────────────────────────────────
        // Set true for the duration of GameDataApiService.RefreshGameAsync().
        // Used by RefreshGameCommand (Schedule/PostseasonViewModel) to no-op a
        // second tap while a refresh is in flight. Not yet wired into the
        // refresh-icon XAML (icon doesn't spin), but the guard works regardless.
        private bool _isRefreshing;
        public bool IsRefreshing
        {
            get => _isRefreshing;
            set { _isRefreshing = value; OnPropertyChanged(); }
        }

        // ── Game detail expand ────────────────────────────────────────────

        private bool _isDetailsExpanded;
        public bool IsDetailsExpanded
        {
            get => _isDetailsExpanded;
            set { _isDetailsExpanded = value; OnPropertyChanged(); OnPropertyChanged(nameof(DetailsExpandIcon)); }
        }

        public string DetailsExpandIcon => _isDetailsExpanded ? "▲" : "▼";

        // ── Rivalry Notes expand ──────────────────────────────────────────
        // Visibility is NOT decided here (unlike ShowDetails above) — GameResult
        // has no access to EntitlementService, and the visibility rule needs both
        // this data AND Season Pass status. See RivalryNotesVisibilityConverter,
        // bound via MultiBinding in XAML instead.

        private RivalryNotes? _rivalryNotes;
        public RivalryNotes? RivalryNotes
        {
            get => _rivalryNotes;
            set { _rivalryNotes = value; OnPropertyChanged(); }
        }

        private bool _isRivalryNotesExpanded;
        public bool IsRivalryNotesExpanded
        {
            get => _isRivalryNotesExpanded;
            set { _isRivalryNotesExpanded = value; OnPropertyChanged(); OnPropertyChanged(nameof(RivalryExpandIcon)); }
        }

        public string RivalryExpandIcon => _isRivalryNotesExpanded ? "▲" : "▼";

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
