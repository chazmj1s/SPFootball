using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Threading;
using System.Windows.Input;
using SaturdayPulse.Helpers;
using SaturdayPulse.Models;
using SaturdayPulse.Services;

namespace SaturdayPulse.ViewModels
{
    public class ScheduleViewModel : BaseViewModel
    {
        private readonly GameDataCacheService         _cache;
        private readonly SharedNavigationStateService _navState;
        private readonly PersonalGameService          _personalGameService;
        private readonly EntitlementService           _entitlementService;

        private ObservableRangeCollection<GameResult> _games = new();
        private bool   _isBusy;
        private bool   _isActive;
        private CancellationTokenSource? _autoRefreshCts;
        private string _activeFilter   = "All";
        private string _selectedFilter = "All";
        private string _statusMessage  = "Loading...";
        private string _emptyMessage   = "Loading...";

        public ScheduleViewModel(
            GameDataCacheService cache,
            FollowService followService,
            SharedNavigationStateService navState,
            PersonalGameService personalGameService,
            EntitlementService entitlementService)
            : base(followService)
        {
            _cache               = cache;
            _navState            = navState;
            _personalGameService = personalGameService;
            _entitlementService  = entitlementService;

            // No outer Task.Run — LoadDataAsync runs on the main thread; the cache
            // fetch inside it is offloaded via Task.Run and the continuation
            // (ApplyFiltersAndSort) returns to the main thread.
            LoadDataCommand = new Microsoft.Maui.Controls.Command(() => _ = LoadDataAsync());

            // Explicitly owns IsRefreshing rather than reusing IsBusy — see
            // the property's doc comment above for why sharing IsBusy here
            // would leave the spinner stuck. If LoadDataAsync no-ops because
            // IsBusy is already true (e.g. an auto-refresh tick was already
            // in flight), this still clears IsRefreshing in the finally, so
            // the spinner dismisses immediately rather than hanging.
            RefreshCommand = new Microsoft.Maui.Controls.Command(async () =>
            {
                IsRefreshing = true;
                try
                {
                    await LoadDataAsync(forceReload: true);
                }
                finally
                {
                    IsRefreshing = false;
                }
            });

            SelectFilterCommand = new Microsoft.Maui.Controls.Command(async () =>
            {
                var options = new List<string> { "All", "P4", "G5" };
                var result  = await Shell.Current.DisplayActionSheet(
                    "Filter", "Cancel", null, options.ToArray());
                if (result != null && result != "Cancel")
                {
                    _activeFilter  = result;
                    SelectedFilter = result;
                    ApplyFiltersAndSort();
                }
            });

            PreviousWeekCommand = new Microsoft.Maui.Controls.Command(() =>
            {
                var idx = _navState.Weeks.ToList().FindIndex(w => w.Week == _navState.SelectedWeek);
                if (idx > 0) _navState.SelectedWeek = _navState.Weeks[idx - 1].Week;
            });

            NextWeekCommand = new Microsoft.Maui.Controls.Command(() =>
            {
                var idx = _navState.Weeks.ToList().FindIndex(w => w.Week == _navState.SelectedWeek);
                if (idx < _navState.Weeks.Count - 1)
                    _navState.SelectedWeek = _navState.Weeks[idx + 1].Week;
            });

            TogglePersonalGameCommand = new Microsoft.Maui.Controls.Command<GameResult>(game =>
            {
                if (game == null) return;
                _personalGameService.Toggle(game.AwayId, game.HomeId);
                game.IsGameFavorited = _personalGameService.IsFavorited(game.AwayId, game.HomeId);
            });

            ToggleDetailsCommand = new Microsoft.Maui.Controls.Command<GameResult>(game =>
            {
                if (game == null) return;
                game.IsDetailsExpanded = !game.IsDetailsExpanded;
            });

            // Mirrors ToggleDetailsCommand exactly — separate expand state, same
            // shape. Visibility (entitlement + data) is decided in XAML via
            // RivalryNotesVisibilityConverter, not here.
            ToggleRivalryNotesCommand = new Microsoft.Maui.Controls.Command<GameResult>(game =>
            {
                if (game == null) return;
                game.IsRivalryNotesExpanded = !game.IsRivalryNotesExpanded;
            });

            // Gated Details paywall message (2026-07-25) — same shared
            // login-check as MyTeamsViewModel/SettingsViewModel. The Details
            // section itself stays open for everyone (per design); only the
            // Vegas/projections portion inside it is replaced with this
            // paywall message for free users.
            SeasonPassCommand = new Microsoft.Maui.Controls.Command(async () =>
            {
                var result = await _entitlementService.EnsureLoggedInForPurchaseAsync();
                if (!result.CanProceed) return;

                await Shell.Current.DisplayAlert(
                    "Season Pass", "Coming soon — payment isn't wired up yet.", "OK");
            });

            _navState.PropertyChanged += OnNavStateChanged;
            _cache.CacheUpdated       += OnCacheUpdated;
            _entitlementService.EntitlementChanged += OnEntitlementChanged;
            _navState.GameHighlightRequested += OnGameHighlightRequested;
        }

        /// <summary>My Teams' opponent-name navigation (2026-09-05). Deferred
        /// via BeginInvokeOnMainThread rather than run synchronously — the
        /// Conference/Week changes the caller just made each queue their own
        /// ApplyFiltersAndSort via OnNavStateChanged, and those ReplaceRange
        /// calls fire a CollectionView Reset that snaps scroll position back
        /// to the top. Queuing this after them (same-thread FIFO order)
        /// means the scroll-to happens last and actually sticks.</summary>
        private void OnGameHighlightRequested(int gameId)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                ApplyFiltersAndSort();

                var match = Games.FirstOrDefault(g => g.Id == gameId);
                if (match == null) return;

                foreach (var g in Games) g.IsHighlighted = false;
                match.IsHighlighted = true;
                ScrollToGameRequested?.Invoke(match);
            });
        }

        /// <summary>Raised once the target game is found and flagged — SchedulePage.xaml.cs scrolls to it.</summary>
        public event Action<GameResult>? ScrollToGameRequested;

        private void OnEntitlementChanged()
        {
            OnPropertyChanged(nameof(HasSeasonPass));
            OnPropertyChanged(nameof(IsNotSeasonPass));
        }

        // ── Bindable collections ──────────────────────────────────────────

        /// <summary>
        /// ObservableRangeCollection fires a single Reset notification on
        /// ReplaceRange instead of one per item — significantly faster
        /// CollectionView re-renders on week/conference/filter changes.
        /// </summary>
        public ObservableRangeCollection<GameResult> Games
        {
            get => _games;
            private set { _games = value; OnPropertyChanged(); }
        }

        // ── Bindable properties ───────────────────────────────────────────

        public bool IsBusy
        {
            get => _isBusy;
            set { _isBusy = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsLoading)); }
        }

        public bool   IsLoading => _isBusy;
        public bool   HasLoaded { get; set; }

        // ── Pull-to-refresh spinner ──────────────────────────────────────
        // Deliberately separate from IsBusy, not a two-way bind of IsBusy
        // itself. RefreshView.IsRefreshing defaults to TwoWay - if bound
        // directly to IsBusy, the native pull gesture would push IsBusy=true
        // into the VM before RefreshCommand's handler runs, and
        // LoadDataAsync's own "if (IsBusy) return;" guard would bail out
        // immediately without ever reaching the finally that clears it,
        // leaving the spinner stuck permanently. This property is set/cleared
        // explicitly by RefreshCommand below and bound OneWay in XAML so the
        // ViewModel is the sole source of truth for it.
        private bool _isRefreshing;
        public bool IsRefreshing
        {
            get => _isRefreshing;
            private set { _isRefreshing = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// True only while Games is the visible tab. Set by MainPage on tab
        /// switch — same pattern as MyTeamsViewModel/PowerRankingsViewModel/
        /// PostseasonViewModel's IsActive. Also starts/stops the client-side
        /// timed refresh (see StartAutoRefresh/StopAutoRefresh below) so it
        /// only runs while someone's actually looking at this tab.
        /// </summary>
        public bool IsActive
        {
            get => _isActive;
            set
            {
                if (_isActive == value) return;
                _isActive = value;
                if (_isActive) StartAutoRefresh();
                else StopAutoRefresh();
            }
        }

        // ── Season Pass gating (2026-07-25) ─────────────────────────────
        // Sourced from the shared EntitlementService. Schedule has no
        // ranking toggle bar (that's MyTeams/Rankings' concern) — this
        // only gates the Vegas/projections portion of the Details paywall
        // message in SchedulePage.xaml.
        public bool HasSeasonPass => _entitlementService.HasSeasonPass;

        /// <summary>Inverse of HasSeasonPass — no inverse-bool converter needed in XAML.</summary>
        public bool IsNotSeasonPass => !HasSeasonPass;

        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }
        public string EmptyMessage
        {
            get => _emptyMessage;
            set { _emptyMessage = value; OnPropertyChanged(); }
        }
        public string SelectedFilter
        {
            get => _selectedFilter;
            set { _selectedFilter = value; OnPropertyChanged(); }
        }

        // ── Commands ──────────────────────────────────────────────────────

        public ICommand LoadDataCommand           { get; }
        public ICommand RefreshCommand            { get; }
        public ICommand SelectFilterCommand       { get; }
        public ICommand PreviousWeekCommand       { get; }
        public ICommand NextWeekCommand           { get; }
        public ICommand TogglePersonalGameCommand { get; }
        public ICommand ToggleDetailsCommand      { get; }
        public ICommand ToggleRivalryNotesCommand { get; }
        public ICommand SeasonPassCommand         { get; }

        // ── Load ──────────────────────────────────────────────────────────

        public async Task LoadDataAsync(bool forceReload = false)
        {
            if (IsBusy) return;
            IsBusy = true;
            StatusMessage = "Loading...";
            EmptyMessage  = "Loading...";

            try
            {
                var games = await Task.Run(() => _cache.GetGamesForYearAsync(_navState.SelectedYear, forceReload));
                if (games == null || games.Count == 0)
                {
                    StatusMessage = "No games found";
                    EmptyMessage  = "No games found";
                    return;
                }

                ApplyFiltersAndSort();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                EmptyMessage  = $"Error: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        // ── Client-side timed refresh (Games tab only) ──────────────────────
        // Re-pulls this year's games from OUR OWN API every AutoRefreshInterval
        // while Games is the visible tab — never calls CFBD directly, that's
        // GameScorePollingService's job server-side. This just picks up
        // whatever it already wrote (scores, Status/Period/Clock) via the
        // same forceReload path RefreshCommand already uses manually. Started/
        // stopped by the IsActive setter above.

        private static readonly TimeSpan AutoRefreshInterval = TimeSpan.FromSeconds(150);

        private void StartAutoRefresh()
        {
            StopAutoRefresh(); // defensive — never run two loops at once
            _autoRefreshCts = new CancellationTokenSource();
            _ = AutoRefreshLoopAsync(_autoRefreshCts.Token);
        }

        private void StopAutoRefresh()
        {
            _autoRefreshCts?.Cancel();
            _autoRefreshCts?.Dispose();
            _autoRefreshCts = null;
        }

        private async Task AutoRefreshLoopAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    await Task.Delay(AutoRefreshInterval, token);
                    if (token.IsCancellationRequested) break;

                    // LoadDataAsync's own IsBusy guard prevents overlap with a
                    // manual pull-to-refresh or an in-flight tick.
                    await LoadDataAsync(forceReload: true);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected — StopAutoRefresh cancels this on tab switch away.
            }
        }

        // ── Filter / sort ─────────────────────────────────────────────────

        private void ApplyFiltersAndSort()
        {
            if (_cache.AllGames.Count == 0)
            {
                _games.ReplaceRange(Enumerable.Empty<GameResult>());
                StatusMessage = "No games found";
                EmptyMessage  = "No games found";
                return;
            }

            IEnumerable<GameResult> filtered = _cache.AllGames;

            filtered = filtered.Where(g => g.Week == _navState.SelectedWeek);

            var conf = _navState.SelectedConference;
            if (conf != "All")
            {
                filtered = filtered.Where(g =>
                    g.HomeConf.Equals(conf, StringComparison.OrdinalIgnoreCase) ||
                    g.AwayConf.Equals(conf, StringComparison.OrdinalIgnoreCase));
            }

            filtered = _activeFilter switch
            {
                "Favorites" => filtered.Where(g => g.IsGameFavorited),
                "Followed"  => filtered.Where(g => g.HomeIsFollowed || g.VisitorIsFollowed),
                "P4"        => filtered.Where(g => g.HomeTier == "P4" || g.AwayTier == "P4"),
                "G5"        => filtered.Where(g => g.HomeTier == "G5" || g.AwayTier == "G5"),
                _           => filtered
            };

            List<GameResult> sorted;
            if (_navState.ShowFavoritesFirst)
            {
                sorted = filtered
                    .OrderByDescending(g => g.IsInProgress)
                    .ThenByDescending(g => g.IsGameFavorited)
                    .ThenByDescending(g => g.HomeIsFollowed || g.VisitorIsFollowed)
                    .ThenBy(g => g.IsFinal)
                    .ThenBy(g => g.SequenceNumber)
                    .ToList();
            }
            else
            {
                sorted = filtered
                    .OrderByDescending(g => g.IsInProgress)
                    .ThenBy(g => g.IsFinal)
                    .ThenBy(g => g.SequenceNumber)
                    .ToList();
            }

            // IsOddRow assigned here — same loop, same "final display order"
            // guarantee as ShowGroupHeader above — rather than derived from
            // SequenceNumber (fixed at load, unrelated to rendered position).
            // rowIndex counts only real game rows, not day-group headers, so
            // shading alternates strictly row-to-row regardless of how many
            // headers fall between them (headers aren't part of this visual
            // rhythm at all — matches PowerRankingsViewModel's flat `i % 2`,
            // just adapted for Schedule's grouped/filtered list).
            string? lastHeader = null;
            var rowIndex = 0;
            foreach (var g in sorted)
            {
                g.ShowGroupHeader = g.GroupHeader != lastHeader;
                lastHeader = g.GroupHeader;
                g.IsOddRow = rowIndex % 2 == 1;
                rowIndex++;
            }

            // ReplaceRange fires single Reset notification — much faster than
            // replacing the entire ObservableCollection reference
            _games.ReplaceRange(sorted);

            StatusMessage = "( ) = projected value";
            EmptyMessage  = "No games for selected filter";
            HasLoaded     = true;
        }

        // ── Event handlers ────────────────────────────────────────────────

        private async void OnNavStateChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != "FilterChanged") return;
            System.Diagnostics.Debug.WriteLine($"[Schedule] FilterChanged reason={_navState.LastFilterChange} isMain={MainThread.IsMainThread}");

            switch (_navState.LastFilterChange)
            {
                case FilterChangeReason.Year:
                    // New year — Main built the week strip from a lightweight query,
                    // but the full game data is Schedule's responsibility. Fetch it.
                    await LoadDataAsync();
                    break;

                case FilterChangeReason.Week:
                case FilterChangeReason.Conference:
                    // The year's games are already cached — just refilter.
                    MainThread.BeginInvokeOnMainThread(ApplyFiltersAndSort);
                    break;
            }
        }

        private void OnCacheUpdated()
        {
            // Only refilter after initial load — avoids double render on startup
            if (!HasLoaded) return;
            MainThread.BeginInvokeOnMainThread(ApplyFiltersAndSort);
        }
    }

    // ── Week selector item ────────────────────────────────────────────────

    public class WeekItem : INotifyPropertyChanged
    {
        private bool _isSelected;

        public int    Week  { get; init; }
        public string Label => $"Wk{Week}";

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
