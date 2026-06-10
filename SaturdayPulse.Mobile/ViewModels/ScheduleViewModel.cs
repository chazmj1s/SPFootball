using System.Collections.ObjectModel;
using System.ComponentModel;
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

        private ObservableCollection<GameResult> _games = new();
        private bool   _isBusy;
        private string _activeFilter   = "All";
        private string _selectedFilter = "All";
        private string _statusMessage  = "Loading...";
        private string _emptyMessage   = "Loading...";

        public ScheduleViewModel(
            GameDataCacheService cache,
            FollowService followService,
            SharedNavigationStateService navState,
            PersonalGameService personalGameService)
            : base(followService)
        {
            _cache               = cache;
            _navState            = navState;
            _personalGameService = personalGameService;

            LoadDataCommand = new Microsoft.Maui.Controls.Command(() => _ = Task.Run(async () => await LoadDataAsync()));
            RefreshCommand = new Microsoft.Maui.Controls.Command(() => _ = Task.Run(async () => await LoadDataAsync(forceReload: true)));


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

            // Week prev/next — delegate to navState, fires FilterChanged(Week)
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

            _navState.PropertyChanged += OnNavStateChanged;
            _cache.CacheUpdated       += OnCacheUpdated;
        }

        // ── Bindable collections ──────────────────────────────────────────

        public ObservableCollection<GameResult> Games
        {
            get => _games;
            set { _games = value; OnPropertyChanged(); }
        }

        // ── Bindable properties ───────────────────────────────────────────

        public bool IsBusy
        {
            get => _isBusy;
            set { _isBusy = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsLoading)); }
        }

        public bool   IsLoading => _isBusy;
        public bool   HasLoaded { get; set; }

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

        // ── Load ──────────────────────────────────────────────────────────

        /// <summary>
        /// Forces a cache reload (e.g. pull-to-refresh) then refilters.
        /// Normal navigation uses ApplyFiltersAndSort directly via FilterChanged —
        /// MainViewModel pre-warms the cache before firing that signal.
        /// </summary>
        public async Task LoadDataAsync(bool forceReload = false)
        {
            if (IsBusy) return;
            IsBusy = true;
            StatusMessage = "Loading...";
            EmptyMessage  = "Loading...";

            try
            {
                var games = await _cache.GetGamesForYearAsync(_navState.SelectedYear, forceReload);
                if (games == null || games.Count == 0)
                {
                    StatusMessage = "No games found";
                    EmptyMessage  = "No games found";
                    return;
                }

                // Populate week strip
                var weeks = games.Select(g => g.Week).Distinct().OrderBy(w => w).ToList();
                _navState.SetWeeks(weeks);
                _navState.ApplyStartupDefaults(
                    games,
                    g => g.Week,
                    g =>
                    {
                        if (string.IsNullOrWhiteSpace(g.GameDate)) return null;
                        var dateStr = $"{g.GameDate} {_navState.SelectedYear}";
                        return DateTime.TryParse(dateStr, out var d) ? d : (DateTime?)null;
                    });

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

        // ── Filter / sort ─────────────────────────────────────────────────

        private void ApplyFiltersAndSort()
        {
            if (_cache.AllGames.Count == 0)
            {
                Games = new ObservableCollection<GameResult>();
                StatusMessage = "No games found";
                EmptyMessage  = "No games found";
                return;
            }

            IEnumerable<GameResult> filtered = _cache.AllGames;

            filtered = filtered.Where(g => g.Week == _navState.SelectedWeek);

            // SelectedConference stores Abbreviation directly — no DisplayToAbbr needed
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
                    .OrderByDescending(g => g.IsGameFavorited)
                    .ThenByDescending(g => g.HomeIsFollowed || g.VisitorIsFollowed)
                    .ThenBy(g => g.SequenceNumber)
                    .ToList();
            }
            else
            {
                sorted = filtered.OrderBy(g => g.SequenceNumber).ToList();
            }

            string? lastHeader = null;
            foreach (var g in sorted)
            {
                g.ShowGroupHeader = g.GroupHeader != lastHeader;
                lastHeader = g.GroupHeader;
            }

            Games         = new ObservableCollection<GameResult>(sorted);
            StatusMessage = "( ) = projected value";
            EmptyMessage  = "No games for selected filter";
            HasLoaded     = true;
        }

        // ── Event handlers ────────────────────────────────────────────────

        private void OnNavStateChanged(object? sender, PropertyChangedEventArgs e)
        {
            // Single entry point — cache is always hot when FilterChanged fires
            if (e.PropertyName == "FilterChanged")
            {
                System.Diagnostics.Debug.WriteLine($"[Schedule] FilterChanged isMain={MainThread.IsMainThread}");

                MainThread.BeginInvokeOnMainThread(ApplyFiltersAndSort);
            }
        }

        private void OnCacheUpdated()
        {
            // Follow/favorite flags re-stamped — refilter to update UI
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
