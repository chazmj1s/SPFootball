using SaturdayPulse.Helpers;
using SaturdayPulse.Models;
using SaturdayPulse.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;

namespace SaturdayPulse.ViewModels
{
    public class PowerRankingsViewModel : BaseViewModel
    {
        private readonly GameDataApiService           _apiService;
        private readonly SharedNavigationStateService _navState;
        private List<TeamRanking> _allTeams = new();
        private ObservableCollection<TeamRanking> _filteredTeams = new();
        private bool          _isBusy;
        private RankingFilter _currentFilter         = RankingFilter.All;
        private RankingSort   _currentSort           = RankingSort.PowerRating;
        private bool          _isSortAscending       = false;
        private string        _selectedFilterDisplay = "All";

        public PowerRankingsViewModel(
            GameDataApiService apiService,
            FollowService followService,
            SharedNavigationStateService navState)
            : base(followService)
        {
            _apiService = apiService;
            _navState   = navState;

            LoadDataCommand       = new Command(async () => await LoadDataAsync());
            RefreshCommand        = new Command(async () => await LoadDataAsync());
            ApplyFilterCommand    = new Command<string>(ApplyFilter);
            ApplySortCommand      = new Command<RankingSort>(ApplySort);
            SortColumnCommand     = new Command<string>(SortByColumn);

            SelectFilterCommand = new Command(async () =>
            {
                var options = new List<string> { "All", "Top 25", "── Tier ──", "P4", "G5", "Independent" };
                var result = await Shell.Current.DisplayActionSheet(
                    "Filter", "Cancel", null, options.ToArray());

                if (result != null && result != "Cancel" && !result.StartsWith("──"))
                {
                    SelectedFilterDisplay = result;
                    ApplyFilter(result == "Top 25" ? "Top25" : result);
                }
            });

            ToggleStatsExpandCommand = new Command<TeamRanking>(t =>
            {
                if (t == null) return;
                t.IsStatsExpanded = !t.IsStatsExpanded;
            });

            ToggleTrendExpandCommand = new Command<TeamRanking>(async t =>
            {
                if (t == null) return;

                if (!t.IsTrendExpanded && t.TrendHistory == null)
                {
                    var data = await _apiService.GetTeamRollingAveragesAsync(
                        t.TeamID, _navState.SelectedYear);

                    if (data?.History?.Count > 0)
                    {
                        var h = data.History[^1];
                        t.TrendRating     = h.TrendRating;
                        t.PedigreeRating  = h.PedigreeRating;
                        t.SeedRating      = h.SeedRating;
                        t.TrendHistory    = h.TrendHistory;
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
                    var data = await _apiService.GetTeamSeasonArcAsync(
                        t.TeamID, _navState.SelectedYear);

                    if (data?.Weeks?.Count > 0)
                        t.SeasonArcWeeks = data.Weeks;
                }

                t.IsArcExpanded = !t.IsArcExpanded;
            });

            ToggleScheduleExpandCommand = new Command<TeamRanking>(async t =>
            {
                if (t == null) return;

                if (!t.IsScheduleExpanded && t.ScheduleGames == null)
                {
                    var data = await _apiService.GetTeamScheduleAsync(
                        t.TeamID, _navState.SelectedYear);

                    if (data?.Games?.Count > 0)
                        t.ScheduleGames = data.Games;
                }

                t.IsScheduleExpanded = !t.IsScheduleExpanded;
            });


            _navState.PropertyChanged += OnNavStateChanged;
            _followService.TeamFollowChanged += OnTeamFollowChanged;
        }

        // ── Bindable collections ──────────────────────────────────────────

        public ObservableCollection<TeamRanking> FilteredTeams
        {
            get => _filteredTeams;
            set { _filteredTeams = value; OnPropertyChanged(); }
        }

        // ── Bindable properties ───────────────────────────────────────────

        public bool IsBusy
        {
            get => _isBusy;
            set { _isBusy = value; OnPropertyChanged(); }
        }

        public string SelectedFilterDisplay
        {
            get => _selectedFilterDisplay;
            set { _selectedFilterDisplay = value; OnPropertyChanged(); }
        }

        public string StatusMessage { get; private set; } = "Loading...";
        public bool   HasLoaded     { get; set; }

        public string ActiveSortLabel => _currentSort switch
        {
            RankingSort.PowerRating => "Rating",
            RankingSort.SOS        => "SOS",
            RankingSort.Record     => "Record",
            RankingSort.TierRank   => "Tier",
            RankingSort.Rank       => "Rank",
            _                      => "Rating"
        };

        public string GetActiveSortValue(TeamRanking t) => _currentSort switch
        {
            RankingSort.PowerRating => t.DisplayRank,
            RankingSort.SOS        => t.DisplaySOS,
            RankingSort.Record     => t.Record,
            RankingSort.TierRank   => t.DisplayTierWithRank,
            RankingSort.Rank       => t.DisplayRank,
            _                      => t.DisplayRank
        };

        // ── Commands ──────────────────────────────────────────────────────

        public ICommand LoadDataCommand          { get; }
        public ICommand RefreshCommand           { get; }
        public ICommand ApplyFilterCommand       { get; }
        public ICommand ApplySortCommand         { get; }
        public ICommand SortColumnCommand        { get; }
        public ICommand SelectFilterCommand      { get; }
        public ICommand ToggleStatsExpandCommand { get; }
        public ICommand ToggleTrendExpandCommand { get; }
        public ICommand ToggleArcExpandCommand   { get; }
        public ICommand ToggleScheduleExpandCommand { get; }

        // ── Load ──────────────────────────────────────────────────────────

        private CancellationTokenSource? _loadCts;

        public async Task LoadDataAsync()
        {
            _loadCts?.Cancel();
            _loadCts = new CancellationTokenSource();
            var token = _loadCts.Token;

            IsBusy = true;
            StatusMessage = "Loading rankings...";
            OnPropertyChanged(nameof(StatusMessage));

            try
            {
                var teams = await _apiService.GetPowerRankingsAsync(
                    _navState.SelectedYear,
                    _navState.SelectedWeek);

                if (token.IsCancellationRequested) return;

                if (teams != null)
                {
                    _allTeams = teams;

                    var followedIds = _followService.GetFollowedIds();
                    foreach (var t in _allTeams)
                    {
                        t.IsFollowed = followedIds.Contains(t.TeamID);
                        t.IsTop25    = t.OverallRank > 0 && t.OverallRank <= 25;
                    }

                    ApplyFiltersAndSort();
                    StatusMessage = _navState.SelectedWeek > 0
                        ? $"{teams.Count} teams · Wk {_navState.SelectedWeek}"
                        : $"{teams.Count} teams · Final";
                }
                else if (!teams.Any())
                {
                    _allTeams.Clear();
                    ApplyFiltersAndSort();
                    StatusMessage = "No rankings available";
                }
                else
                {
                    _allTeams.Clear();
                    ApplyFiltersAndSort();
                    StatusMessage = "Failed to load rankings";
                }

                HasLoaded = true;
            }
            catch (Exception ex)
            {
                if (token.IsCancellationRequested) return;
                System.Diagnostics.Debug.WriteLine(ex.Message?.ToString());
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                if (!token.IsCancellationRequested)
                {
                    IsBusy = false;
                    OnPropertyChanged(nameof(StatusMessage));
                }
            }
        }

        // ── Filter / sort ─────────────────────────────────────────────────

        public void ApplyFilter(string filterType)
        {
            _currentFilter = filterType switch
            {
                "Top25"       => RankingFilter.Top25,
                "P4"          => RankingFilter.P4,
                "G5"          => RankingFilter.G5,
                "Independent" => RankingFilter.Independent,
                _             => RankingFilter.All
            };
            ApplyFiltersAndSort();
        }

        public void ApplySort(RankingSort sortType)
        {
            _currentSort = sortType;
            OnPropertyChanged(nameof(ActiveSortLabel));
            ApplyFiltersAndSort();
        }

        public void SortByColumn(string columnName)
        {
            var newSort = columnName switch
            {
                "Rank"       => RankingSort.Rank,
                "Team"       => RankingSort.TeamName,
                "Record"     => RankingSort.Record,
                "Rating"     => RankingSort.PowerRating,
                "Conference" => RankingSort.Conference,
                "SOS"        => RankingSort.SOS,
                "TierRank"   => RankingSort.TierRank,
                "Tier"       => RankingSort.Tier,
                _            => RankingSort.Rank
            };

            if (_currentSort == newSort)
                _isSortAscending = !_isSortAscending;
            else
            {
                _currentSort = newSort;
                _isSortAscending = newSort switch
                {
                    RankingSort.PowerRating => false,
                    RankingSort.SOS        => false,
                    _                      => true
                };
            }

            ApplyFiltersAndSort();
            OnPropertyChanged(nameof(ActiveSortLabel));
        }

        private void ApplyFiltersAndSort()
        {
            var filtered = _allTeams.AsEnumerable();

            // Conference filter
            var conf = _navState.SelectedConference;
            if (conf != "All")
            {
                var abbr = ConferenceHelper.DisplayToAbbr(conf);
                filtered = filtered.Where(t =>
                    (t.ConferenceAbbr != null &&
                        t.ConferenceAbbr.Equals(abbr, StringComparison.OrdinalIgnoreCase)) ||
                    (t.Conference != null &&
                        t.Conference.Equals(abbr, StringComparison.OrdinalIgnoreCase)));
            }

            // Tier / top-25 filter
            filtered = _currentFilter switch
            {
                RankingFilter.Top25       => filtered.Where(t => t.IsTop25),
                RankingFilter.P4          => filtered.Where(t => t.Tier == "P4"),
                RankingFilter.G5          => filtered.Where(t => t.Tier == "G5"),
                RankingFilter.Independent => filtered.Where(t => t.Tier == "Independent"),
                _                         => filtered
            };

            // Column sort
            IOrderedEnumerable<TeamRanking> sorted = _currentSort switch
            {
                RankingSort.Rank => _isSortAscending
                    ? filtered.OrderBy(t => t.OverallRank)
                    : filtered.OrderByDescending(t => t.OverallRank),
                RankingSort.TeamName => _isSortAscending
                    ? filtered.OrderBy(t => t.TeamName)
                    : filtered.OrderByDescending(t => t.TeamName),
                RankingSort.PowerRating => _isSortAscending
                    ? filtered.OrderBy(t => t.Ranking ?? 0)
                    : filtered.OrderByDescending(t => t.Ranking ?? 0),
                RankingSort.Record => _isSortAscending
                    ? filtered.OrderBy(t => t.Wins).ThenBy(t => t.Losses)
                    : filtered.OrderByDescending(t => t.Wins).ThenBy(t => t.Losses),
                RankingSort.Conference => _isSortAscending
                    ? filtered.OrderBy(t => t.Conference).ThenBy(t => t.OverallRank)
                    : filtered.OrderByDescending(t => t.Conference).ThenBy(t => t.OverallRank),
                RankingSort.SOS => _isSortAscending
                    ? filtered.OrderBy(t => t.CombinedSOS ?? 0)
                    : filtered.OrderByDescending(t => t.CombinedSOS ?? 0),
                RankingSort.TierRank => _isSortAscending
                    ? filtered.OrderBy(t => t.TierRank)
                    : filtered.OrderByDescending(t => t.TierRank),
                RankingSort.Tier => _isSortAscending
                    ? filtered.OrderBy(t => t.Tier).ThenBy(t => t.TierRank)
                    : filtered.OrderByDescending(t => t.Tier).ThenBy(t => t.TierRank),
                _ => filtered.OrderBy(t => t.OverallRank)
            };

            // ShowFavoritesFirst: float followed teams to top, preserve sort within each group
            var result = _navState.ShowFavoritesFirst
                ? sorted.OrderByDescending(t => t.IsFollowed).ToList()
                : sorted.ToList();

            for (int i = 0; i < result.Count; i++)
            {
                result[i].ActiveSortValue = GetActiveSortValue(result[i]);
                result[i].IsOddRow = i % 2 == 1;
                result[i].IsTop25  = result[i].OverallRank > 0 && result[i].OverallRank <= 25;
            }

            FilteredTeams = new ObservableCollection<TeamRanking>(result);
        }

        private async void OnNavStateChanged(object sender, PropertyChangedEventArgs e)
        {
            // User week tap — re-filter client-side only, no server call
            if (e.PropertyName == nameof(SharedNavigationStateService.SelectedWeek))
                await LoadDataAsync();

            if (e.PropertyName == nameof(SharedNavigationStateService.SelectedConference) ||
                e.PropertyName == nameof(SharedNavigationStateService.ShowFavoritesFirst))
                ApplyFiltersAndSort();
        }

        private void OnTeamFollowChanged(int teamId, bool isFollowed)
        {
            var team = _allTeams.FirstOrDefault(t => t.TeamID == teamId);
            if (team != null)
            {
                team.IsFollowed = isFollowed;
                if (_navState.ShowFavoritesFirst)
                    MainThread.BeginInvokeOnMainThread(ApplyFiltersAndSort);
            }
        }
    }
}
