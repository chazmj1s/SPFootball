using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using NCAA_Power_Ratings.Mobile.Helpers;
using NCAA_Power_Ratings.Mobile.Models;
using NCAA_Power_Ratings.Mobile.Services;

namespace NCAA_Power_Ratings.Mobile.ViewModels
{
    public class PowerRankingsViewModel : BaseViewModel
    {
        private readonly GameDataApiService    _apiService;
        private readonly SharedNavigationStateService _navState;
        private List<TeamRanking> _allTeams = new();
        private ObservableCollection<TeamRanking> _filteredTeams = new();
        private bool          _isBusy;
        private RankingFilter _currentFilter   = RankingFilter.All;
        private RankingSort   _currentSort     = RankingSort.PowerRating;
        private bool          _isSortAscending = false;
        private string        _selectedFilterDisplay = "All";

        public PowerRankingsViewModel(
            GameDataApiService apiService,
            FollowService followService,
            SharedNavigationStateService navState)
            : base(followService)
        {
            _apiService = apiService;
            _navState   = navState;

            LoadDataCommand   = new Microsoft.Maui.Controls.Command(async () => await LoadDataAsync());
            RefreshCommand    = new Microsoft.Maui.Controls.Command(async () => await LoadDataAsync());
            ApplyFilterCommand = new Microsoft.Maui.Controls.Command<string>(ApplyFilter);
            ApplySortCommand   = new Microsoft.Maui.Controls.Command<RankingSort>(ApplySort);
            SortColumnCommand  = new Microsoft.Maui.Controls.Command<string>(SortByColumn);

            SelectFilterCommand = new Microsoft.Maui.Controls.Command(async () =>
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

            // React to shared nav changes
            _navState.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(SharedNavigationStateService.SelectedYear) ||
                e.PropertyName == nameof(SharedNavigationStateService.SelectedWeek))
                    _ = LoadDataAsync();
                if (e.PropertyName == nameof(SharedNavigationStateService.SelectedConference))
                    ApplyFiltersAndSort();
            };

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
        public bool   HasLoaded    { get; private set; }

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

        public ICommand LoadDataCommand      { get; }
        public ICommand RefreshCommand       { get; }
        public ICommand ApplyFilterCommand   { get; }
        public ICommand ApplySortCommand     { get; }
        public ICommand SortColumnCommand    { get; }
        public ICommand SelectFilterCommand  { get; }

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
                else
                {
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
            switch (filterType)
            {
                case "All":
                    _currentFilter = RankingFilter.All;
                    break;
                case "Top25":
                    _currentFilter = RankingFilter.Top25;
                    break;
                case "P4":
                    _currentFilter = RankingFilter.P4;
                    break;
                case "G5":
                    _currentFilter = RankingFilter.G5;
                    break;
                case "Independent":
                    _currentFilter = RankingFilter.Independent;
                    break;
                default:
                    _currentFilter = RankingFilter.All;
                    break;
            }
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

            // Conference filter from shared nav state
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

            // Additional filter (All/Top25/P4/G5/Independent)
            filtered = _currentFilter switch
            {
                RankingFilter.Top25       => filtered.Where(t => t.IsTop25),
                RankingFilter.P4          => filtered.Where(t => t.Tier == "P4"),
                RankingFilter.G5          => filtered.Where(t => t.Tier == "G5"),
                RankingFilter.Independent => filtered.Where(t => t.Tier == "Independent"),
                _                         => filtered
            };

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

            var result = sorted.ToList();
            foreach (var team in result)
                team.ActiveSortValue = GetActiveSortValue(team);

            for (int i = 0; i < result.Count; i++)
            {
                result[i].IsOddRow = i % 2 == 1;
                result[i].IsTop25  = result[i].OverallRank > 0 && result[i].OverallRank <= 25;
            }

            FilteredTeams = new ObservableCollection<TeamRanking>(result);
        }

        private void OnTeamFollowChanged(int teamId, bool isFollowed)
        {
            var team = _allTeams.FirstOrDefault(t => t.TeamID == teamId);
            if (team != null) team.IsFollowed = isFollowed;
        }
    }
}
