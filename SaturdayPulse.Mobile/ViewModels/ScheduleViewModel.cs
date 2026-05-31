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
        private readonly GameDataApiService           _apiService;
        private readonly SharedNavigationStateService _navState;
        private readonly PersonalGameService          _personalGameService;

        private List<GameResult>                 _allGames = new();
        private ObservableCollection<GameResult> _games    = new();
        private bool   _isBusy;
        private string _activeFilter   = "All";
        private string _selectedFilter = "All";
        private string _statusMessage  = string.Empty;

        public ScheduleViewModel(
            GameDataApiService apiService,
            FollowService followService,
            SharedNavigationStateService navState,
            PersonalGameService personalGameService)
            : base(followService)
        {
            _apiService          = apiService;
            _navState            = navState;
            _personalGameService = personalGameService;

            LoadDataCommand = new Microsoft.Maui.Controls.Command(async () => await LoadDataAsync());
            RefreshCommand  = new Microsoft.Maui.Controls.Command(async () => await LoadDataAsync());

            SelectFilterCommand = new Microsoft.Maui.Controls.Command(async () =>
            {
                var options = new List<string> { "All", "P4", "G5" };
                var result  = await Shell.Current.DisplayActionSheet(
                    "Filter", "Cancel", null, options.ToArray());
                if (result != null && result != "Cancel")
                {
                    _activeFilter = result;
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

            _navState.PropertyChanged                += OnNavStateChanged;
            _followService.TeamFollowChanged         += OnTeamFollowChanged;
            _personalGameService.GameFavoritedChange += OnGameFavoritedChange;
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
        public bool   HasLoaded { get; set; }  // public setter so MainPage can reset on year change

        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
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



        // ── Load ──────────────────────────────────────────────────────────

        public async Task LoadDataAsync()
        {
            if (IsBusy) return;
            IsBusy = true;
            StatusMessage = "Loading...";

            try
            {
                var games = await _apiService.GetScheduleAsync(_navState.SelectedYear);
                if (games == null || games.Count == 0)
                {
                    StatusMessage = "No games found";
                    return;
                }

                for (int i = 0; i < games.Count; i++)
                    games[i].SequenceNumber = i + 1;

                _allGames = games;

                var followedIds = _followService.GetFollowedIds();
                foreach (var g in _allGames)
                {
                    g.HomeIsFollowed    = followedIds.Contains(g.HomeId);
                    g.VisitorIsFollowed = followedIds.Contains(g.AwayId);
                    g.IsGameFavorited   = _personalGameService.IsFavorited(g.AwayId, g.HomeId);
                }

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
                StatusMessage = "( ) = projected value";
                HasLoaded = true;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {

                IsBusy = false;
            }
        }

        // ── Filter / sort ─────────────────────────────────────────────────

        private void ApplyFiltersAndSort()
        {
            IEnumerable<GameResult> filtered = _allGames;

            filtered = filtered.Where(g => g.Week == _navState.SelectedWeek);

            var conf = _navState.SelectedConference;
            if (conf != "All")
            {
                var abbr = ConferenceHelper.DisplayToAbbr(conf);
                filtered = filtered.Where(g =>
                    g.HomeConf.Equals(abbr, StringComparison.OrdinalIgnoreCase) ||
                    g.AwayConf.Equals(abbr,  StringComparison.OrdinalIgnoreCase));
            }

            filtered = _activeFilter switch
            {
                "All"       => filtered,
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

            string lastHeader = null;
            foreach (var g in sorted)
            {
                g.ShowGroupHeader = g.GroupHeader != lastHeader;
                lastHeader = g.GroupHeader;
            }

            Games = new ObservableCollection<GameResult>(sorted);
        }

        // ── Event handlers ────────────────────────────────────────────────

        private void OnNavStateChanged(object sender, PropertyChangedEventArgs e)
        {
            // User week tap — re-filter client-side only, no server call
            if (e.PropertyName == nameof(SharedNavigationStateService.SelectedWeek))
                ApplyFiltersAndSort();

            if (e.PropertyName == nameof(SharedNavigationStateService.SelectedConference) ||
                e.PropertyName == nameof(SharedNavigationStateService.ShowFavoritesFirst))
                ApplyFiltersAndSort();
        }

        private void OnTeamFollowChanged(int teamId, bool isFollowed)
        {
            foreach (var g in _allGames)
            {
                if (g.HomeId == teamId) g.HomeIsFollowed    = isFollowed;
                if (g.AwayId == teamId) g.VisitorIsFollowed = isFollowed;
            }

            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (_activeFilter == "My Teams" || _navState.ShowFavoritesFirst)
                    ApplyFiltersAndSort();
                else
                {
                    var temp = Games;
                    Games = null;
                    Games = temp;
                }
            });
        }

        private void OnGameFavoritedChange(string key, bool isFollowed)
        {
            foreach (var g in _allGames)
            {
                if (PersonalGameService.Key(g.AwayId, g.HomeId) == key)
                    g.IsGameFavorited = isFollowed;
            }

            if (_navState.ShowFavoritesFirst)
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
