using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using NCAA_Power_Ratings.Mobile.Helpers;
using NCAA_Power_Ratings.Mobile.Models;
using NCAA_Power_Ratings.Mobile.Services;

namespace NCAA_Power_Ratings.Mobile.ViewModels
{
    public class ScheduleViewModel : BaseViewModel
    {
        private readonly GameDataApiService    _apiService;
        private readonly SharedNavigationStateService _navState;
        private List<GameResult> _allGames = new();
        private ObservableCollection<GameResult> _games = new();
        private bool   _isBusy;
        private string _activeFilter   = "All";
        private string _selectedFilter = "All";
        private string _statusMessage  = string.Empty;

        public ScheduleViewModel(
            GameDataApiService apiService,
            FollowService followService,
            SharedNavigationStateService navState)
            : base(followService)
        {
            _apiService = apiService;
            _navState   = navState;

            LoadDataCommand = new Microsoft.Maui.Controls.Command(async () => await LoadDataAsync());
            RefreshCommand  = new Microsoft.Maui.Controls.Command(async () => await LoadDataAsync());

            SelectFilterCommand = new Microsoft.Maui.Controls.Command(async () =>
            {
                var options = new List<string> { "All", "Followed", "P4", "G5" };

                var result = await Shell.Current.DisplayActionSheet(
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

            _navState.PropertyChanged += OnNavStateChanged;
            _followService.TeamFollowChanged += OnTeamFollowChanged;
        }

        private void OnNavStateChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SharedNavigationStateService.SelectedYear))
                _ = LoadDataAsync();
            if (e.PropertyName == nameof(SharedNavigationStateService.SelectedWeek) ||
                e.PropertyName == nameof(SharedNavigationStateService.SelectedConference))
                ApplyFiltersAndSort();
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

        public ICommand LoadDataCommand     { get; }
        public ICommand RefreshCommand      { get; }
        public ICommand SelectFilterCommand { get; }
        public ICommand PreviousWeekCommand { get; }
        public ICommand NextWeekCommand     { get; }

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

            // Week filter
            filtered = filtered.Where(g => g.Week == _navState.SelectedWeek);

            // Conference filter from shared nav state
            var conf = _navState.SelectedConference;
            if (conf != "All")
            {
                var abbr = ConferenceHelper.DisplayToAbbr(conf);
                filtered = filtered.Where(g =>
                    g.WinnerConf.Equals(abbr, StringComparison.OrdinalIgnoreCase) ||
                    g.LoserConf.Equals(abbr,  StringComparison.OrdinalIgnoreCase));
            }

            // Additional filter (All/Followed/P4/G5)
            filtered = _activeFilter switch
            {
                "All"      => filtered,
                "Followed" => filtered.Where(g => g.WinnerIsFollowed || g.LoserIsFollowed),
                "P4"       => filtered.Where(g => g.WinnerTier == "P4" || g.LoserTier == "P4"),
                "G5"       => filtered.Where(g => g.WinnerTier == "G5" || g.LoserTier == "G5"),
                _          => filtered
            };

            var sorted = filtered.OrderBy(g => g.SequenceNumber).ToList();

            string lastHeader = null;
            foreach (var g in sorted)
            {
                g.ShowGroupHeader = g.GroupHeader != lastHeader;
                lastHeader = g.GroupHeader;
            }

            Games = new ObservableCollection<GameResult>(sorted);
        }

        // ── Follow sync ───────────────────────────────────────────────────

        private void OnTeamFollowChanged(int teamId, bool isFollowed)
        {
            foreach (var g in _allGames)
            {
                if (g.WinnerId == teamId) g.WinnerIsFollowed = isFollowed;
                if (g.LoserId  == teamId) g.LoserIsFollowed  = isFollowed;
            }

            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (_activeFilter == "Followed")
                    ApplyFiltersAndSort();
                else
                {
                    var temp = Games;
                    Games = null;
                    Games = temp;
                }
            });
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
