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
        private readonly RankingsCacheService         _rankingsCache;
        private readonly SharedNavigationStateService _navState;
        private List<TeamRanking> _allTeams = new();
        private ObservableCollection<TeamRanking> _filteredTeams = new();
        private bool          _isBusy;
        private RankingFilter _currentFilter         = RankingFilter.All;
        private RankingSort   _currentSort           = RankingSort.PowerRating;
        private bool          _isSortAscending       = false;
        private string        _selectedFilterDisplay = "All";
        private string        _statusMessage = "Loading...";
        private string        _emptyMessage = "Loading...";


        public PowerRankingsViewModel(
            GameDataApiService apiService,
            RankingsCacheService rankingsCache,
            FollowService followService,
            SharedNavigationStateService navState)
            : base(followService)
        {
            _apiService    = apiService;
            _rankingsCache = rankingsCache;
            _navState      = navState;

            // No outer Task.Run — LoadDataAsync runs on the main thread; the HTTP
            // call inside it is offloaded via its own Task.Run, and the continuation
            // (ApplyFiltersAndSort) returns to the main thread.
            LoadDataCommand = new Microsoft.Maui.Controls.Command(() => _ = LoadDataAsync());
            RefreshCommand  = new Microsoft.Maui.Controls.Command(() => _ = LoadDataAsync(forceReload: true));
            ApplyFilterCommand = new Microsoft.Maui.Controls.Command<string>(ApplyFilter);
            ApplySortCommand      = new Microsoft.Maui.Controls.Command<RankingSort>(ApplySort);
            SortColumnCommand     = new Microsoft.Maui.Controls.Command<string>(SortByColumn);

            SelectFilterCommand = new Microsoft.Maui.Controls.Command(async () =>
            {
                var options = new List<string> { "All", "Top 25", "── Tier ──", "P4", "G5", "Independent" };
                
                var result = await Shell.Current.DisplayActionSheet("Filter", "Cancel", null, options.ToArray());

                if (result != null && result != "Cancel" && !result.StartsWith("──"))
                {
                    SelectedFilterDisplay = result;
                    ApplyFilter(result == "Top 25" ? "Top25" : result);
                }
            });

            ToggleStatsExpandCommand = new Microsoft.Maui.Controls.Command<TeamRanking>(t =>
            {
                if (t == null) return;
                t.IsStatsExpanded = !t.IsStatsExpanded;
            });

            ToggleTrendExpandCommand = new Microsoft.Maui.Controls.Command<TeamRanking>(async t =>
            {
                if (t == null) return;

                if (!t.IsTrendExpanded && t.TrendHistory == null)
                {
                    var data = await Task.Run(async () =>
                        await _apiService.GetTeamRollingAveragesAsync(t.TeamID, _navState.SelectedYear));

                    if (data?.History?.Count > 0)
                    {
                        var h = data.History[^1];
                        t.TrendRating = h.TrendRating;
                        t.PedigreeRating = h.PedigreeRating;
                        t.SeedRating = h.SeedRating;
                        t.TrendHistory = h.TrendHistory;
                        t.PedigreeHistory = h.PedigreeHistory;
                    }
                }

                t.IsTrendExpanded = !t.IsTrendExpanded;
            });

            ToggleArcExpandCommand = new Microsoft.Maui.Controls.Command<TeamRanking>(async t =>
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

            ToggleScheduleExpandCommand = new Microsoft.Maui.Controls.Command<TeamRanking>(async t =>
            {
                if (t == null) return;

                if (!t.IsScheduleExpanded && t.ScheduleGames == null)
                {
                    var data = await Task.Run(async () =>
                        await _apiService.GetTeamScheduleAsync(t.TeamID, _navState.SelectedYear));

                    if (data?.Games?.Count > 0)
                        t.ScheduleGames = data.Games;
                }

                t.IsScheduleExpanded = !t.IsScheduleExpanded;
            });

            _navState.PropertyChanged += OnNavStateChanged;
            _followService.TeamFollowChanged += OnTeamFollowChanged;
            _rankingsCache.CacheUpdated += OnRankingsCacheUpdated;
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
        public bool   HasLoaded     { get; set; }

        /// <summary>
        /// True only while Rankings is the visible tab. Set by MainPage on tab
        /// switch. When false, the page defers FilterChanged work (marks itself
        /// stale) instead of loading off-screen — so it never renders during launch
        /// or behind another tab. The lazy SyncPage path loads it on first visit.
        /// </summary>
        public bool   IsActive      { get; set; }

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

        public async Task LoadDataAsync(bool forceReload = false)
        {
            _loadCts?.Cancel();
            _loadCts = new CancellationTokenSource();
            var token = _loadCts.Token;

            IsBusy = true;
            StatusMessage = "Loading rankings...";
            EmptyMessage = "Loading...";
            OnPropertyChanged(nameof(StatusMessage));

            try
            {
                var teams = await Task.Run(async () =>
                    await _rankingsCache.GetRankingsAsync(
                        _navState.SelectedYear,
                        _navState.SelectedWeek,
                        forceReload), token);

                if (token.IsCancellationRequested) return;

                if (teams != null && teams.Any())
                {
                    // Follow/Top25 flags are stamped once by RankingsCacheService
                    // on these shared instances — no per-consumer stamping needed.
                    _allTeams = teams.ToList();

                    ApplyFiltersAndSort();
                    StatusMessage = _navState.SelectedWeek > 0
                        ? $"{teams.Count} teams · Wk {_navState.SelectedWeek}"
                        : $"{teams.Count} teams · Final";
                }
                else
                {
                    _allTeams.Clear();
                    ApplyFiltersAndSort();
                    StatusMessage = "No rankings available";
                    EmptyMessage = "No rankings available";
                }

                HasLoaded = true;
            }
            catch (Exception ex)
            {
                if (token.IsCancellationRequested) return;
                System.Diagnostics.Debug.WriteLine(ex.Message);
                StatusMessage = $"Failed to load rankings. Error: {ex.Message}";
                EmptyMessage = "Failed to load rankings.";
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
                filtered = filtered.Where(t =>
                    (t.ConferenceAbbr != null &&
                     t.ConferenceAbbr.Equals(conf, StringComparison.OrdinalIgnoreCase)) ||
                    (t.Conference != null &&
                     t.Conference.Equals(conf, StringComparison.OrdinalIgnoreCase)));
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
            if (e.PropertyName != "FilterChanged") return;

            System.Diagnostics.Debug.WriteLine($"[Rankings] FilterChanged isMain={MainThread.IsMainThread} isActive={IsActive}");

            // Off-screen: don't load or render now. Mark stale so SyncPage reloads
            // this page the next time it becomes visible.
            if (!IsActive)
            {
                HasLoaded = false;
                return;
            }

            switch (_navState.LastFilterChange)
            {
                case FilterChangeReason.Year:
                case FilterChangeReason.Week:
                    // Year or week changed — rankings are week-specific, must hit server
                    await LoadDataAsync();
                    break;

                case FilterChangeReason.Conference:
                    // Conference or favorites changed — refilter cached results only
                    ApplyFiltersAndSort();
                    break;
            }
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

        /// <summary>
        /// RankingsCacheService also stamps IsFollowed on TeamFollowChanged and
        /// fires CacheUpdated. This is a secondary safety net in case another
        /// consumer (e.g. MyTeamsViewModel) triggers a full cache reload while
        /// Rankings is active — refilter from the now-current shared list rather
        /// than going stale. Guarded by HasLoaded so it doesn't double-fire
        /// immediately after this page's own LoadDataAsync completes.
        /// </summary>
        private void OnRankingsCacheUpdated()
        {
            if (!HasLoaded || !IsActive) return;
            MainThread.BeginInvokeOnMainThread(() =>
            {
                _allTeams = _rankingsCache.AllRankings.ToList();
                ApplyFiltersAndSort();
            });
        }
    }
}
