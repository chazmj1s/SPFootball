using Syncfusion.Licensing;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
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

        /// <summary>Sequential position assigned by the ViewModel after load — used for "original order" sort tiebreaker only, NOT display order.</summary>
        public int  SequenceNumber { get; set; }

        // Was `=> SequenceNumber % 2 == 1` — SequenceNumber is fixed at load
        // time and only ever used as a sort tiebreaker (see ScheduleViewModel
        // BuildFilteredList's ThenBy), so it has no relationship to a game's
        // actual rendered row position once IsFinal/favorited/followed
        // ordering and day-grouping are applied. Now assigned explicitly by
        // ScheduleViewModel after final sort, same as TeamRanking.IsOddRow
        // in PowerRankingsViewModel — see 2026-09 chat.
        private bool _isOddRow;
        public bool IsOddRow
        {
            get => _isOddRow;
            set { _isOddRow = value; OnPropertyChanged(); }
        }

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
                OnPropertyChanged(nameof(DisplayMarginValue));
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
                OnPropertyChanged(nameof(DisplayMarginValue));
                OnPropertyChanged(nameof(HomeIsWinner));
            }
        }

        public double? AwayProjScore { get; set; }

        public char    Location  { get; set; }   // 'H' = has home team, 'N' = neutral
        public bool    IsPlayed  { get; set; }
        public int     ActualOU  { get; set; }
        public double? ProjOU { get; set; }
        public double? ProjMargin { get; set; }

        // ── Live status (scorecard clock line, 2026-09-05) ─────────────────
        // Not yet populated end-to-end — GetScheduleAsync/RefreshGameAsync's
        // DTOs don't carry these fields yet, so Status/Period/Clock stay null
        // until the API side is wired up. DisplayGameStatus resolves to
        // string.Empty in that case and the XAML row hides itself via
        // StringNotEmptyConverter (same pattern as DisplayGameTime).
        // Full properties (not auto-properties), same reasoning as
        // HomePoints/AwayPoints: a future poll/refresh update on an
        // already-bound instance needs to repaint the label.
        private string? _status;
        public string? Status
        {
            get => _status;
            set
            {
                if (_status == value) return;
                _status = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayGameStatus));
                OnPropertyChanged(nameof(DisplayGameTimeOrStatus));
                OnPropertyChanged(nameof(IsFinal));
                OnPropertyChanged(nameof(IsInProgress));
            }
        }

        private int? _period;
        public int? Period
        {
            get => _period;
            set
            {
                if (_period == value) return;
                _period = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayGameStatus));
                OnPropertyChanged(nameof(DisplayGameTimeOrStatus));
            }
        }

        private string? _clock;
        public string? Clock
        {
            get => _clock;
            set
            {
                if (_clock == value) return;
                _clock = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayGameStatus));
                OnPropertyChanged(nameof(DisplayGameTimeOrStatus));
            }
        }

        // ── Quarter-by-quarter line scores (2026-09-13) ─────────────────────
        // Full properties (not auto-properties) — GameScorePollingService
        // upserts these every ~2 minutes for an in-progress game (see
        // GameScorePollingService.PollIfInWindowAsync), same live-refresh
        // reasoning as HomePoints/AwayPoints/Status above. Null coalesced to
        // an empty list on set so QN/OT accessors never need a null check.
        // History coverage on these two columns is unconfirmed as of this
        // change — QN/OT accessors below return "" for any missing index,
        // which also covers older games that never had line scores recorded.
        private List<int> _homeLineScores = new();
        public List<int> HomeLineScores
        {
            get => _homeLineScores;
            set
            {
                _homeLineScores = value ?? new List<int>();
                OnPropertyChanged();
                OnPropertyChanged(nameof(HomeQ1));
                OnPropertyChanged(nameof(HomeQ2));
                OnPropertyChanged(nameof(HomeQ3));
                OnPropertyChanged(nameof(HomeQ4));
                OnPropertyChanged(nameof(HomeOT));
                OnPropertyChanged(nameof(HasOvertime));
                OnPropertyChanged(nameof(OvertimeCount));
                OnPropertyChanged(nameof(OvertimeHeaderText));
            }
        }

        private List<int> _awayLineScores = new();
        public List<int> AwayLineScores
        {
            get => _awayLineScores;
            set
            {
                _awayLineScores = value ?? new List<int>();
                OnPropertyChanged();
                OnPropertyChanged(nameof(AwayQ1));
                OnPropertyChanged(nameof(AwayQ2));
                OnPropertyChanged(nameof(AwayQ3));
                OnPropertyChanged(nameof(AwayQ4));
                OnPropertyChanged(nameof(AwayOT));
                OnPropertyChanged(nameof(HasOvertime));
                OnPropertyChanged(nameof(OvertimeCount));
                OnPropertyChanged(nameof(OvertimeHeaderText));
            }
        }

        /// <summary>Returns "" for a quarter that hasn't been played/recorded yet rather than throwing on a short list.</summary>
        private static string QuarterScoreAt(List<int> lineScores, int quarterIndex) =>
            lineScores.Count > quarterIndex ? lineScores[quarterIndex].ToString(CultureInfo.InvariantCulture) : string.Empty;

        /// <summary>
        /// CFBD gives each overtime period its own array slot (confirmed
        /// against the 2018 Texas A&amp;M/LSU 7-OT game: 11-entry arrays,
        /// index 4-10) but the card only has room for one OT column. Shown
        /// as "{points before the deciding period} ({deciding period's
        /// points})" — e.g. a 7-OT game with periods 3,7,8,3,6,8,6 renders
        /// as "35 (6)". For the far more common single-OT case this
        /// collapses to just the period's score ("6"), since "0 (6)" reads
        /// oddly when there's nothing to sum before it. The authoritative
        /// final score is always HomePoints/AwayPoints, not a sum of this
        /// column — a game whose OT column undercounts due to a data gap
        /// still shows the correct Total.
        /// </summary>
        private static string OvertimeDisplay(List<int> lineScores)
        {
            if (lineScores.Count <= 4) return string.Empty;

            var otPeriods = lineScores.Skip(4).ToList();
            var lastPeriod = otPeriods[^1];

            return otPeriods.Count == 1
                ? lastPeriod.ToString(CultureInfo.InvariantCulture)
                : $"{otPeriods.Take(otPeriods.Count - 1).Sum()} ({lastPeriod})";
        }

        public string AwayQ1 => QuarterScoreAt(AwayLineScores, 0);
        public string AwayQ2 => QuarterScoreAt(AwayLineScores, 1);
        public string AwayQ3 => QuarterScoreAt(AwayLineScores, 2);
        public string AwayQ4 => QuarterScoreAt(AwayLineScores, 3);
        public string AwayOT => OvertimeDisplay(AwayLineScores);

        public string HomeQ1 => QuarterScoreAt(HomeLineScores, 0);
        public string HomeQ2 => QuarterScoreAt(HomeLineScores, 1);
        public string HomeQ3 => QuarterScoreAt(HomeLineScores, 2);
        public string HomeQ4 => QuarterScoreAt(HomeLineScores, 3);
        public string HomeOT => OvertimeDisplay(HomeLineScores);

        /// <summary>True if either side's line scores carry a 5th (OT) entry — gates the OT column/header in XAML.</summary>
        public bool HasOvertime => AwayLineScores.Count > 4 || HomeLineScores.Count > 4;

        /// <summary>
        /// Number of overtime periods played, taken from whichever side's
        /// list is longer (defensive against a mismatch — both sides should
        /// always match in practice, since periods are played simultaneously).
        /// 0 when there was no overtime.
        /// </summary>
        public int OvertimeCount => Math.Max(AwayLineScores.Count, HomeLineScores.Count) - 4 is var n && n > 0 ? n : 0;

        /// <summary>Header label for the OT column — "OT (7)" for the Texas A&amp;M/LSU game, "OT (1)" for a normal single-OT game, empty (column hidden via HasOvertime) otherwise.</summary>
        public string OvertimeHeaderText => HasOvertime ? $"OT ({OvertimeCount})" : string.Empty;

        /// <summary>
        /// "Final" when Status is "completed" (case-insensitive); "Halftime"
        /// when Period is 2 and Clock has hit zero; otherwise
        /// "{Clock} {periodLabel}" (e.g. "12:34 2nd", "0:42 OT").
        /// Empty when Status hasn't been populated — used as the visibility
        /// gate in XAML rather than a separate bool.
        /// </summary>
        public string DisplayGameStatus
        {
            get
            {
                if (string.IsNullOrEmpty(Status)) return string.Empty;

                if (Status.Equals("completed", StringComparison.OrdinalIgnoreCase))
                    return "Final";

                if (Period == 2 && IsClockZero(Clock))
                    return "Halftime";

                var period = Period ?? 0;
                var periodLabel = period switch
                {
                    1 => "1st",
                    2 => "2nd",
                    3 => "3rd",
                    4 => "4th",
                    5 => "OT",
                    > 5 => $"{period - 4}OT",
                    _ => string.Empty
                };

                return string.IsNullOrEmpty(periodLabel)
                    ? Clock ?? string.Empty
                    : $"{Clock} {periodLabel}";
            }
        }

        /// <summary>
        /// Kickoff time when the game hasn't started (Status null/empty or
        /// "scheduled"); DisplayGameStatus otherwise (which already resolves
        /// to "Final" for completed games and clock/period for in-progress
        /// ones). Lets one label occupy the space DisplayGameTime and
        /// DisplayGameStatus used to split between them (2026-09-13 card
        /// refactor).
        /// </summary>
        public string DisplayGameTimeOrStatus =>
            string.IsNullOrEmpty(Status) || Status.Equals("scheduled", StringComparison.OrdinalIgnoreCase)
                ? DisplayGameTime
                : DisplayGameStatus;

        /// <summary>Matches "0:00" / "00:00" without a TimeSpan parse — CFBD's clock field is a plain "M:SS" string.</summary>
        private static bool IsClockZero(string? clock) =>
            !string.IsNullOrEmpty(clock) && clock.TrimStart('0', ':').Length == 0;

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

        // ── Prefix-less margin/O-U (2026-09-13 card refactor) ───────────────
        // Same values as DisplayMargin/DisplayOU but without the "Margin: "/
        // "O/U: " label — the new combined-line layout carries that label in
        // the card's column header instead ("Margin : O/U"), so repeating it
        // per row would just eat width. DisplayMargin/DisplayOU are left
        // in place above in case anything else still binds to them.
        public string DisplayMarginValue => IsPlayed
            ? $"{ActualMargin} ({DisplayProjMargin})"
            : $"({DisplayProjMargin})";
        public string DisplayOUValue => IsPlayed
            ? $"{ActualOU} ({DisplayProjOU})"
            : $"({DisplayProjOU})";

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
                if (string.IsNullOrEmpty(GameDate))
                    return $"Week {Week}";

                return DateTime.TryParseExact(GameDate, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var d)
                    ? d.ToString("ddd, MMM %d", CultureInfo.InvariantCulture)
                    : GameDate;
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

        // ── Cross-tab highlight (2026-09-05) ────────────────────────────
        // Set true on the single game targeted by My Teams' opponent-name
        // navigation (see ScheduleViewModel.OnGameHighlightRequested). Not
        // persisted, not filtered on — purely a visual cue consumed by
        // SchedulePage.xaml's DataTrigger.
        private bool _isHighlighted;
        public bool IsHighlighted
        {
            get => _isHighlighted;
            set { _isHighlighted = value; OnPropertyChanged(); }
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

        /// <summary>
        /// True only once the game has actually concluded. Distinct from
        /// IsPlayed, which flips true as soon as CFBD starts reporting a
        /// score — i.e. also true for in-progress games — so IsPlayed alone
        /// isn't safe to sort "completed games to the bottom" on. Falls back
        /// to IsPlayed when Status hasn't been populated (games from before
        /// the live-status pipeline, or outside the poller's today-only
        /// window), so older weeks still group correctly.
        /// </summary>
        public bool IsFinal =>
            !string.IsNullOrEmpty(Status)
                ? Status.Equals("completed", StringComparison.OrdinalIgnoreCase)
                : IsPlayed;

        /// <summary>
        /// True only while a game is actively being played right now — used
        /// to pin live games to the very top of Schedule regardless of
        /// favorited/followed status (see ScheduleViewModel.BuildFilteredList).
        /// Requires Status to be populated and to be neither "scheduled" nor
        /// "completed"; games from before the live-status pipeline (Status
        /// null) or not yet kicked off are never "in progress" here.
        /// </summary>
        public bool IsInProgress =>
            !string.IsNullOrEmpty(Status)
            && !Status.Equals("completed", StringComparison.OrdinalIgnoreCase)
            && !Status.Equals("scheduled", StringComparison.OrdinalIgnoreCase);

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
