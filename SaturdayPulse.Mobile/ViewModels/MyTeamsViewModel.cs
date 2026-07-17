using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using Microsoft.Maui.Controls;
using SaturdayPulse.Helpers;
using SaturdayPulse.Models;
using SaturdayPulse.Services;

namespace SaturdayPulse.ViewModels
{
    /// <summary>
    /// Drives the My Teams page: team chip scroller, single-team rankings
    /// card, single-team schedule list.
    ///
    /// Rankings and games are both sourced from shared caches
    /// (RankingsCacheService, GameDataCacheService) — the same instances
    /// PowerRankingsViewModel and ScheduleViewModel use — so My Teams never
    /// issues its own network call on a team switch; it just refilters the
    /// already-warm shared lists down to SelectedTeamId.
    ///
    /// IMPORTANT — shared TeamRanking instances: because rankings come from
    /// a shared cache, the SAME TeamRanking object can be rendered both here
    /// (as the header card) and in PowerRankingsPage's list. This ViewModel
    /// deliberately never touches IsOddRow (that's PowerRankingsViewModel's
    /// list-position concern) to avoid corrupting the other page's zebra
    /// striping. Expand state (IsTrendExpanded/IsArcExpanded/IsStatsExpanded)
    /// and lazily-fetched history (TrendHistory/SeasonArcWeeks) ARE shared
    /// across both pages by design — expanding a panel here means it's
    /// already-expanded (and already-fetched) if you flip to Rankings for
    /// the same team, and vice versa.
    ///
    /// IsActive mirrors PowerRankingsViewModel's pattern exactly: only true
    /// while My Teams is the visible tab. When false, FilterChanged work is
    /// deferred (marked stale via HasLoaded = false) rather than loading
    /// off-screen. Set by MainViewModel on tab switch.
    /// </summary>
    public class MyTeamsViewModel : BaseViewModel
    {
        private readonly GameDataApiService           _apiService;
        private readonly GameDataCacheService         _gameCache;
        private readonly RankingsCacheService         _rankingsCache;
        private readonly TeamCacheService              _teamCache;
        private readonly PersonalGameService           _personalGameService;
        private readonly SharedNavigationStateService  _navState;

        private ObservableRangeCollection<MyTeamsGameRow> _selectedTeamGames = new();
        private int             _selectedTeamId;
        private TeamRanking?    _selectedTeamRanking;
        private bool            _isBusy;
        private string          _statusMessage = "Loading...";
        private string          _emptyMessage  = "Follow a team, or set a default team in Settings, to get started.";

        public MyTeamsViewModel(
            GameDataApiService apiService,
            GameDataCacheService gameCache,
            RankingsCacheService rankingsCache,
            TeamCacheService teamCache,
            FollowService followService,
            PersonalGameService personalGameService,
            SharedNavigationStateService navState)
            : base(followService)
        {
            _apiService          = apiService;
            _gameCache           = gameCache;
            _rankingsCache       = rankingsCache;
            _teamCache           = teamCache;
            _personalGameService = personalGameService;
            _navState            = navState;

            SelectTeamCommand = new Command<int>(teamId =>
            {
                if (teamId == 0 || teamId == SelectedTeamId) return;
                SelectedTeamId = teamId;
                UpdateChipSelection();
                ApplyTeamFilter(); // no network call — both caches already warm
            });

            RefreshCommand = new Command(() => _ = LoadForYearOrWeekChangeAsync(forceReload: true));

            // Tapping a game sets the week selector to that game's week —
            // _navState is shared with MainViewModel, so this has the exact
            // same effect as tapping the week strip directly.
            SelectWeekCommand = new Command<int>(week =>
            {
                if (week > 0) _navState.SelectedWeek = week;
            });

            // Copied from PowerRankingsViewModel's expand commands exactly —
            // same lazy-fetch-once-then-toggle shape, same TeamRanking fields.
            ToggleTrendExpandCommand = new Command<TeamRanking>(async t =>
            {
                if (t == null) return;

                if (!t.IsTrendExpanded && t.TrendHistory == null)
                {
                    var data = await Task.Run(async () =>
                        await _apiService.GetTeamRollingAveragesAsync(t.TeamID, _navState.SelectedYear));

                    if (data?.History?.Count > 0)
                    {
                        var h = data.History[^1];
                        t.TrendRating    = h.TrendRating;
                        t.PedigreeRating = h.PedigreeRating;
                        t.SeedRating     = h.SeedRating;
                        t.TrendHistory   = h.TrendHistory;
                        t.PedigreeHistory = h.PedigreeHistory;
                    }
                }

                t.IsTrendExpanded = !t.IsTrendExpanded;
            });

            ToggleArcExpandCommand = new Command<TeamRanking>(async t =>
            {
                if (t == null) return;

                if (!t.IsArcExpanded && t.SeasonArcWeeks == null)
                {
                    var data = await Task.Run(async () =>
                        await _apiService.GetTeamSeasonArcAsync(t.TeamID, _navState.SelectedYear));

                    if (data?.Weeks?.Count > 0)
                        t.SeasonArcWeeks = data.Weeks;
                }

                t.IsArcExpanded = !t.IsArcExpanded;
            });

            ToggleStatsExpandCommand = new Command<TeamRanking>(t =>
            {
                if (t == null) return;
                t.IsStatsExpanded = !t.IsStatsExpanded;
            });

            // Mirrors ScheduleViewModel's TogglePersonalGameCommand exactly.
            TogglePersonalGameCommand = new Command<GameResult>(game =>
            {
                if (game == null) return;
                _personalGameService.Toggle(game.AwayId, game.HomeId);
                game.IsGameFavorited = _personalGameService.IsFavorited(game.AwayId, game.HomeId);
            });

            // Mirrors ScheduleViewModel's ToggleDetailsCommand exactly.
            ToggleDetailsCommand = new Command<GameResult>(game =>
            {
                if (game == null) return;
                game.IsDetailsExpanded = !game.IsDetailsExpanded;
            });

            _followService.TeamFollowChanged  += OnTeamFollowChanged;
            _followService.PrimaryTeamChanged += OnPrimaryTeamChanged;
            _navState.PropertyChanged         += OnNavStateChanged;
            _gameCache.CacheUpdated           += OnSharedCacheUpdated;
            _rankingsCache.CacheUpdated       += OnSharedCacheUpdated;
        }

        // ── Bindable collections ──────────────────────────────────────────

        public ObservableRangeCollection<MyTeamsGameRow> SelectedTeamGames
        {
            get => _selectedTeamGames;
            private set { _selectedTeamGames = value; OnPropertyChanged(); }
        }

        public ObservableCollection<TeamChipItem> Chips { get; } = new();

        // ── Bindable properties ───────────────────────────────────────────

        public int SelectedTeamId
        {
            get => _selectedTeamId;
            private set { _selectedTeamId = value; OnPropertyChanged(); }
        }

        public TeamRanking? SelectedTeamRanking
        {
            get => _selectedTeamRanking;
            private set
            {
                _selectedTeamRanking = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSelectedTeamRanking));
            }
        }

        /// <summary>
        /// The pinned team card row (outside the CollectionView) binds its
        /// IsVisible here rather than to SelectedTeamRanking directly, since
        /// that card sets its own BindingContext to SelectedTeamRanking —
        /// this property has to be read off the page's BindingContext
        /// (MyTeamsViewModel), one level up.
        /// </summary>
        public bool HasSelectedTeamRanking => SelectedTeamRanking != null;

        public bool IsBusy
        {
            get => _isBusy;
            private set { _isBusy = value; OnPropertyChanged(); }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            private set { _statusMessage = value; OnPropertyChanged(); }
        }

        public string EmptyMessage
        {
            get => _emptyMessage;
            private set { _emptyMessage = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// True only while My Teams is the visible tab. Set by MainPage on tab
        /// switch — same role as PowerRankingsViewModel.IsActive.
        /// </summary>
        public bool IsActive { get; set; }

        /// <summary>
        /// Set true once InitializeAsync/LoadForYearOrWeekChangeAsync has
        /// completed at least once. MainPage.xaml.cs resets this to false on
        /// year change (ResetAllPages) and checks it in SyncPage's lazy-load
        /// switch — same convention as ScheduleViewModel/PowerRankingsViewModel.
        /// </summary>
        public bool HasLoaded { get; set; }

        // ── Commands ──────────────────────────────────────────────────────

        public ICommand SelectTeamCommand         { get; }
        public ICommand SelectWeekCommand          { get; }
        public ICommand RefreshCommand             { get; }
        public ICommand ToggleTrendExpandCommand   { get; }
        public ICommand ToggleArcExpandCommand     { get; }
        public ICommand ToggleStatsExpandCommand   { get; }
        public ICommand ToggleDetailsCommand       { get; }
        public ICommand TogglePersonalGameCommand  { get; }
        // ToggleFollowCommand is inherited from BaseViewModel — already
        // pattern-matches int (GameCardTemplate hearts) and TeamRanking
        // (TeamCardTemplate heart).

        // ── Initial load ──────────────────────────────────────────────────

        /// <summary>
        /// Call once on first navigation to this page. Since My Teams is the
        /// default landing page, InitializeAsync's TeamCacheService warm-up
        /// is effectively the app's first data load.
        /// </summary>
        public async Task InitializeAsync()
        {
            IsBusy = true;
            try
            {
                await _teamCache.EnsureLoadedAsync();
                BuildChips();

                if (Chips.Count == 0)
                {
                    StatusMessage = "No teams followed yet.";
                    return;
                }

                if (SelectedTeamId == 0)
                    SelectedTeamId = Chips[0].TeamId;

                UpdateChipSelection();
                await LoadForYearOrWeekChangeAsync();
            }
            finally
            {
                IsBusy = false;
            }
        }

        // ── Reload paths ──────────────────────────────────────────────────

        /// <summary>
        /// Year or Week changed (or explicit refresh) — warms both shared
        /// caches for the current year/week, then refilters to the selected
        /// team. Both cache calls no-op server-side if already warm and
        /// forceReload is false.
        /// </summary>
        private async Task LoadForYearOrWeekChangeAsync(bool forceReload = false)
        {
            if (SelectedTeamId == 0) return;

            IsBusy = true;
            try
            {
                var year = _navState.SelectedYear;
                var week = _navState.SelectedWeek;

                await _rankingsCache.GetRankingsAsync(year, week, forceReload);
                await _gameCache.GetGamesForYearAsync(year, forceReload);

                HasLoaded = true;
                ApplyTeamFilter();
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void ApplyTeamFilter()
        {
            SelectedTeamRanking = _rankingsCache.AllRankings
                .FirstOrDefault(r => r.TeamID == SelectedTeamId);

            var rows = _gameCache.AllGames
                .Where(g => g.AwayId == SelectedTeamId || g.HomeId == SelectedTeamId)
                .OrderBy(g => g.Week)
                .Select(BuildGameRow)
                .ToList();

            SelectedTeamGames.ReplaceRange(rows);

            StatusMessage = SelectedTeamRanking is null
                ? "No ranking data for this team/week yet."
                : string.Empty;
        }

        /// <summary>
        /// Resolves a GameResult into the selected team's perspective —
        /// opponent name/id/follow-state, "my score" vs "their score", short
        /// date. SpreadLine/OULine reuse Game.DisplayMargin/DisplayOU as-is
        /// (already fully-formatted strings, e.g. "Spread: -7 (-2.5)").
        /// </summary>
        private MyTeamsGameRow BuildGameRow(GameResult g)
        {
            bool teamIsHome = g.HomeId == SelectedTeamId;

            var dateShort = TryFormatShortDate(g.GameDate); // see class-header note re: field name assumption

            // Raw HomePoints/AwayPoints (not the pre-formatted Display*Score
            // strings, which append a projection in parens) so this is a
            // clean numeric comparison from the selected team's perspective.
            var resultLetter = string.Empty;
            if (g.IsPlayed)
            {
                var myScore  = teamIsHome ? g.HomePoints : g.AwayPoints;
                var oppScore = teamIsHome ? g.AwayPoints : g.HomePoints;
                resultLetter = myScore > oppScore ? "(W)" : myScore < oppScore ? "(L)" : "(T)";
            }

            return new MyTeamsGameRow
            {
                Game               = g,
                Week               = g.Week,
                DateShort          = dateShort,
                AtPrefix           = teamIsHome ? "vs " : "@ ",
                ResultLetter       = resultLetter,
                OpponentName       = teamIsHome ? g.VisitorName       : g.HomeName,
                OpponentTeamId     = teamIsHome ? g.AwayId            : g.HomeId,
                OpponentIsFollowed = teamIsHome ? g.VisitorIsFollowed : g.HomeIsFollowed,
                ScoreLine          = teamIsHome
                    ? $"{g.DisplayHomeScore} - {g.DisplayVisitorScore}"
                    : $"{g.DisplayVisitorScore} - {g.DisplayHomeScore}",
                SpreadLine         = g.DisplayMargin,
                OULine             = g.DisplayOU,
                IsSelectedWeek     = g.Week == _navState.SelectedWeek
            };
        }

        private static string TryFormatShortDate(string? rawDate) =>
            DateTime.TryParse(rawDate, out var d) ? d.ToString("M/d") : (rawDate ?? string.Empty);

        // ── Chip management ──────────────────────────────────────────────

        private void BuildChips()
        {
            Chips.Clear();

            var primaryId   = _followService.GetPrimaryTeamId();
            var followedIds = _followService.GetFollowedIds();

            if (primaryId.HasValue)
            {
                var team = _teamCache.GetTeam(primaryId.Value);
                if (team != null)
                {
                    Chips.Add(new TeamChipItem
                    {
                        TeamId    = team.TeamID,
                        TeamName  = team.TeamName,
                        IsPrimary = true
                    });
                }
            }

            var followedTeams = followedIds
                .Where(id => id != primaryId)
                .Select(id => _teamCache.GetTeam(id))
                .Where(t => t != null)
                .OrderBy(t => t!.TeamName);

            foreach (var team in followedTeams)
            {
                Chips.Add(new TeamChipItem
                {
                    TeamId    = team!.TeamID,
                    TeamName  = team.TeamName,
                    IsPrimary = false
                });
            }

            UpdateChipSelection();
        }

        private void UpdateChipSelection()
        {
            foreach (var chip in Chips)
                chip.IsSelected = chip.TeamId == SelectedTeamId;
        }

        // ── Event handlers ────────────────────────────────────────────────

        private async void OnNavStateChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != "FilterChanged") return;

            // Off-screen: defer, mark stale so the next appearance reloads —
            // same pattern as PowerRankingsViewModel.
            if (!IsActive)
            {
                HasLoaded = false;
                return;
            }

            switch (_navState.LastFilterChange)
            {
                case FilterChangeReason.Year:
                case FilterChangeReason.Week:
                    await LoadForYearOrWeekChangeAsync();
                    break;

                case FilterChangeReason.Conference:
                    // Ignored — My Teams doesn't use the global conference
                    // filter (see MainViewModel's ConferencePillText context
                    // switch instead).
                    break;
            }
        }

        /// <summary>
        /// Fires from either GameDataCacheService or RankingsCacheService —
        /// covers follow-flag stamps and reloads triggered by other tabs
        /// (e.g. Rankings force-refreshing while My Teams is active).
        /// Refilter only, never reload — reload is LoadForYearOrWeekChangeAsync's job.
        /// </summary>
        private void OnSharedCacheUpdated()
        {
            if (!HasLoaded || !IsActive) return;
            MainThread.BeginInvokeOnMainThread(ApplyTeamFilter);
        }

        private void OnTeamFollowChanged(int teamId, bool isFollowed)
        {
            BuildChips();

            // If the currently-selected team lost its only chip (un-followed
            // and not primary), fall back to the first remaining chip.
            if (!isFollowed && teamId == SelectedTeamId && Chips.All(c => c.TeamId != SelectedTeamId))
            {
                SelectedTeamId = Chips.FirstOrDefault()?.TeamId ?? 0;
                UpdateChipSelection();
                ApplyTeamFilter();
            }
        }

        private async void OnPrimaryTeamChanged(int? teamId)
        {
            BuildChips();

            // Per design: primary-team change is treated as a filter change —
            // re-point at the new team immediately.
            if (teamId.HasValue)
            {
                SelectedTeamId = teamId.Value;
                UpdateChipSelection();

                // If this fires before the very first load ever completed —
                // e.g. FollowService.InitializeAsync() resolving after
                // InitializeAsync() above already hit its "no teams
                // followed yet" early-return because the follow cache
                // wasn't warm yet — the shared rankings/games caches were
                // never fetched. Refiltering an empty cache leaves chips
                // and a primary team visible with no game data, until
                // something else (a tab switch) triggers a real load. Do
                // the real load here instead of just refiltering.
                if (HasLoaded)
                    ApplyTeamFilter();
                else
                    await LoadForYearOrWeekChangeAsync();
            }
        }
    }

    // ── My Teams compact game row ────────────────────────────────────────

    /// <summary>
    /// Wraps a GameResult with the selected team's perspective already
    /// resolved — opponent name/id/follow-state, "my score" vs "their
    /// score", short date — so the compact single-line card in
    /// MyTeamsPage.xaml doesn't need any converters or Home/Away branching
    /// in XAML. Rebuilt fresh every time ApplyTeamFilter runs (team switch,
    /// year/week change, cache update), so plain get-only properties are
    /// fine — no INotifyPropertyChanged needed, the whole row gets replaced.
    ///
    /// Game.IsGameFavorited / SpreadLine / OULine are the SAME pre-formatted
    /// display strings the original card already used (DisplayMargin /
    /// DisplayOU) — reused as-is, not reconstructed from VegasLines.
    ///
    /// ASSUMPTION FLAGGED: DateShort is parsed from GameResult.GameDate,
    /// guessed by analogy with PlayedWeekInfo.GameDate (used elsewhere for
    /// the weeks endpoint) since GroupHeader/ShowGroupHeader clearly derive
    /// from *some* raw date field on GameResult but I don't have that model
    /// file to confirm the actual property name. If GameDate doesn't exist
    /// on GameResult, swap the source field in BuildGameRow below.
    /// </summary>
    public class MyTeamsGameRow
    {
        public GameResult Game               { get; init; } = null!;
        public int         Week               { get; init; }
        public string      DateShort          { get; init; } = string.Empty;
        public string      AtPrefix           { get; init; } = string.Empty; // "@ " or "vs "
        /// <summary>"W", "L", "T", or "" if the game hasn't been played yet. From the selected team's perspective.</summary>
        public string      ResultLetter       { get; init; } = string.Empty;
        public string      OpponentName       { get; init; } = string.Empty;
        public int         OpponentTeamId     { get; init; }
        public bool         OpponentIsFollowed { get; init; }
        public string      ScoreLine          { get; init; } = string.Empty; // "7 (26) - 14 (28)"
        public string      SpreadLine         { get; init; } = string.Empty; // Game.DisplayMargin, reused as-is
        public string      OULine             { get; init; } = string.Empty; // Game.DisplayOU, reused as-is
        public bool         IsSelectedWeek     { get; init; }
    }


    public class TeamChipItem : INotifyPropertyChanged
    {
        private bool _isSelected;

        public int    TeamId    { get; init; }
        public string TeamName  { get; init; } = string.Empty;
        public bool   IsPrimary { get; init; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
