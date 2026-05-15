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
                var options = new List<string> { "All", "My Games", "My Teams", "P4", "G5" };
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
                _personalGameService.Toggle(game.VisitorId, game.HomeId);
                game.IsGameFavorited = _personalGameService.IsFavorited(
                    game.VisitorId, game.HomeId);
            });

            _navState.PropertyChanged              += OnNavStateChanged;
            _followService.TeamFollowChanged       += OnTeamFollowChanged;
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
        public bool   HasLoaded { get; private set; }

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
                    g.WinnerIsFollowed = followedIds.Contains(g.WinnerId);
                    g.LoserIsFollowed  = followedIds.Contains(g.LoserId);
                    g.IsGameFavorited  = _personalGameService.IsFavorited(g.VisitorId, g.HomeId);
                }

                var weeks = games.Select(g => g.Week).Distinct().OrderBy(w => w).ToList();
                _navState.SetWeeks(weeks);
                _navState.SetDefaultWeek(games.Where(g => g.IsPlayed).Select(g => g.Week));

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
                    g.WinnerConf.Equals(abbr, StringComparison.OrdinalIgnoreCase) ||
                    g.LoserConf.Equals(abbr,  StringComparison.OrdinalIgnoreCase));
            }

            filtered = _activeFilter switch
            {
                "All"      => filtered,
                "Favorites" => filtered.Where(g => g.IsGameFavorited),
                "Followed" => filtered.Where(g => g.WinnerIsFollowed || g.LoserIsFollowed),
                "P4"       => filtered.Where(g => g.WinnerTier == "P4" || g.LoserTier == "P4"),
                "G5"       => filtered.Where(g => g.WinnerTier == "G5" || g.LoserTier == "G5"),
                _          => filtered
            };

            // ShowFavoritesFirst: starred games → followed-team games → normal sequence
            List<GameResult> sorted;
            if (_navState.ShowFavoritesFirst)
            {
                sorted = filtered
                    .OrderByDescending(g => g.IsGameFavorited)
                    .ThenByDescending(g => g.WinnerIsFollowed || g.LoserIsFollowed)
                    .ThenBy(g => g.SequenceNumber)
                    .ToList();
            }
            else
            {
                sorted = filtered.OrderBy(g => g.SequenceNumber).ToList();
            }

            // Group headers — stamp after sort so they reflect actual display order
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
            if (e.PropertyName == nameof(SharedNavigationStateService.SelectedYear))
                _ = LoadDataAsync();

            if (e.PropertyName == nameof(SharedNavigationStateService.SelectedWeek) ||
                e.PropertyName == nameof(SharedNavigationStateService.SelectedConference) ||
                e.PropertyName == nameof(SharedNavigationStateService.ShowFavoritesFirst))
                ApplyFiltersAndSort();
        }

        private void OnTeamFollowChanged(int teamId, bool isFollowed)
        {
            foreach (var g in _allGames)
            {
                if (g.WinnerId == teamId) g.WinnerIsFollowed = isFollowed;
                if (g.LoserId  == teamId) g.LoserIsFollowed  = isFollowed;
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
                if (PersonalGameService.Key(g.VisitorId, g.HomeId) == key)
                    g.IsGameFavorited = isFollowed;
            }

            // Re-sort if ShowFavoritesFirst is on so the game moves immediately
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
