using System.Collections.ObjectModel;
using System.Windows.Input;
using SaturdayPulse.Helpers;
using SaturdayPulse.Models;
using SaturdayPulse.Services;

namespace SaturdayPulse.ViewModels
{
    public class SettingsViewModel : BaseViewModel
    {
        private readonly GameDataApiService           _apiService;
        private readonly PersonalGameService          _personalGameService;
        private readonly SharedNavigationStateService _navState;

        // ── Raw data ──────────────────────────────────────────────────────
        private List<TeamInfo>    _allTeams      = [];
        private List<RivalryInfo> _allRivalries  = [];
        private List<RivalryInfo> _personalGames = [];

        // ── Sub-tab state ─────────────────────────────────────────────────
        private string _selectedView = "Teams";

        public string SelectedView
        {
            get => _selectedView;
            set
            {
                if (_selectedView == value) return;
                _selectedView = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsTeamsView));
                OnPropertyChanged(nameof(IsGamesView));
            }
        }

        // ── Accordion state — only one section open at a time ─────────────
        private string? _expandedSection = "Following"; // open by default

        public bool IsFollowingExpanded     => _expandedSection == "Following";
        public bool IsUserConfigExpanded    => _expandedSection == "UserConfig";
        public bool IsMoreCoolStuffExpanded => _expandedSection == "MoreCoolStuff";
        public bool IsDebugLogExpanded      => _expandedSection == "DebugLog";

        public bool IsTeamsView => _selectedView == "Teams";
        public bool IsGamesView => _selectedView == "Games";

        // ── Shared state ──────────────────────────────────────────────────
        private bool   _isBusy;
        private string _statusMessage = string.Empty;

        public bool IsBusy
        {
            get => _isBusy;
            set { _isBusy = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsLoading)); }
        }

        public bool IsLoading => _isBusy;
        public bool HasLoaded { get; private set; }

        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        // ── User preference: Show Favorites First ─────────────────────────
        public bool ShowFavoritesFirst
        {
            get => _navState.ShowFavoritesFirst;
            set => _navState.ShowFavoritesFirst = value;
        }

        public string DefaultWeek
        {
            get => _navState.DefaultWeek;
            set => _navState.DefaultWeek = value;
        }

        public string DefaultConference
        {
            get => _navState.DefaultConference;
            set => _navState.DefaultConference = value;
        }

        // ── Teams ─────────────────────────────────────────────────────────
        public ObservableCollection<TeamInfo> Teams { get; } = new();

        // ── Games ─────────────────────────────────────────────────────────
        public ObservableCollection<RivalryInfo> Games { get; } = new();

        public ObservableCollection<string> TierFilters { get; } = new();

        private string _selectedTier = "♥ Personal";
        public string SelectedTier
        {
            get => _selectedTier;
            set
            {
                if (value == "── Rivalries ──") return;
                if (_selectedTier == value) return;
                _selectedTier = value;
                OnPropertyChanged();
                ApplyGamesFilter();
            }
        }

        // ── Debug Log ─────────────────────────────────────────────────────

        /// <summary>Bound to the Debug Log CollectionView in Settings.</summary>
        public ObservableCollection<LogEntry> LogEntries => AppLogger.Entries;

        public int LogEntryCount => AppLogger.Entries.Count;

        // ── Commands ──────────────────────────────────────────────────────
        public ICommand LoadDataCommand                { get; }
        public ICommand SelectViewCommand              { get; }
        public ICommand TogglePersonalCommand          { get; }
        public ICommand ToggleSectionCommand           { get; }
        public ICommand ToggleFollowCommand            { get; }
        public ICommand RefreshCommand                 { get; }
        public ICommand SelectDefaultWeekCommand       { get; }
        public ICommand SelectDefaultConferenceCommand { get; }
        public ICommand ClearLogCommand                { get; }

        // ── Constructor ───────────────────────────────────────────────────
        public SettingsViewModel(
            GameDataApiService apiService,
            FollowService followService,
            PersonalGameService personalGameService,
            SharedNavigationStateService navState)
            : base(followService)
        {
            _apiService          = apiService;
            _personalGameService = personalGameService;
            _navState            = navState;

            TierFilters.Add("All");
            TierFilters.Add("♥ Personal");
            TierFilters.Add("── Rivalries ──");
            TierFilters.Add("🔥 Epic");
            TierFilters.Add("⭐ National");
            TierFilters.Add("🏠 Regional");
            TierFilters.Add("• Meh");

            // No outer Task.Run — LoadDataAsync runs on the main thread; the team +
            // rivalry fetch inside it is offloaded via Task.Run and the continuation
            // (ApplyTeamFilter / ApplyGamesFilter) returns to the main thread.
            LoadDataCommand = new Microsoft.Maui.Controls.Command(() => _ = LoadDataAsync());
            RefreshCommand  = new Microsoft.Maui.Controls.Command(() => _ = LoadDataAsync());

            SelectViewCommand = new Microsoft.Maui.Controls.Command<string>(view =>
            {
                SelectedView = view;
            });

            TogglePersonalCommand = new Microsoft.Maui.Controls.Command<RivalryInfo>(rivalry =>
            {
                if (rivalry == null) return;
                _personalGameService.Toggle(rivalry.Team1Id, rivalry.Team2Id);
                rivalry.IsGameFavorited = _personalGameService.IsFavorited(
                    rivalry.Team1Id, rivalry.Team2Id);

                if (!rivalry.IsGameFavorited && _selectedTier == "♥ Personal")
                    ApplyGamesFilter();
            });

            // Toggle team follow. FollowService.Toggle flips + persists state and raises
            // TeamFollowChanged, which OnTeamFollowChanged handles to refresh team and
            // rivalry follow flags and re-filter both lists. Drives the Teams-tab follow
            // icon and the per-team hearts on the Games cards.
            ToggleFollowCommand = new Microsoft.Maui.Controls.Command<int>(teamId =>
                _followService.Toggle(teamId));

            ToggleSectionCommand = new Command<string>(section =>
            {
                _expandedSection = _expandedSection == section ? null : section;
                OnPropertyChanged(nameof(IsFollowingExpanded));
                OnPropertyChanged(nameof(IsUserConfigExpanded));
                OnPropertyChanged(nameof(IsMoreCoolStuffExpanded));
                OnPropertyChanged(nameof(IsDebugLogExpanded));
            });

            SelectDefaultWeekCommand = new Microsoft.Maui.Controls.Command<string>(value =>
            {
                DefaultWeek = value;
            });

            SelectDefaultConferenceCommand = new Microsoft.Maui.Controls.Command(async () =>
            {
                var options = new List<string> { "All" };
                options.AddRange(ConferenceHelper.OrderedConferences.Select(c => c.Display));

                var result = await Shell.Current.DisplayActionSheet(
                    "Default Conference", "Cancel", null, options.ToArray());

                if (result != null && result != "Cancel")
                    DefaultConference = result == "All" ? "All"
                        : ConferenceHelper.DisplayToAbbr(result) ?? result;
            });

            ClearLogCommand = new Microsoft.Maui.Controls.Command(() =>
            {
                AppLogger.Clear();
                OnPropertyChanged(nameof(LogEntryCount));
            });

            // Keep LogEntryCount in sync as entries are added/removed
            AppLogger.Entries.CollectionChanged += (s, e) =>
                OnPropertyChanged(nameof(LogEntryCount));

            _followService.TeamFollowChanged         += OnTeamFollowChanged;
            _personalGameService.GameFavoritedChange += OnGameFavoritedChange;

            _navState.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(SharedNavigationStateService.SelectedConference))
                    ApplyTeamFilter();
                if (e.PropertyName == nameof(SharedNavigationStateService.DefaultWeek))
                    OnPropertyChanged(nameof(DefaultWeek));
                if (e.PropertyName == nameof(SharedNavigationStateService.DefaultConference))
                    OnPropertyChanged(nameof(DefaultConference));
            };
        }

        // ── Load ──────────────────────────────────────────────────────────
        public async Task LoadDataAsync()
        {
            if (IsBusy) return;
            IsBusy = true;
            StatusMessage = "Loading...";

            try
            {
                var (teams, rivalries) = await Task.Run(async () =>
                {
                    var teamsTask     = _apiService.GetTeamsAsync();
                    var rivalriesTask = _apiService.GetNamedRivalriesAsync();
                    await Task.WhenAll(teamsTask, rivalriesTask);
                    return (teamsTask.Result, rivalriesTask.Result);
                });

                if (teams != null && teams.Count > 0)
                {
                    foreach (var t in teams)
                        t.IsFollowed = _followService.IsFollowed(t.TeamID);

                    _allTeams = [.. teams.OrderBy(t => t.TeamName)];
                    ApplyTeamFilter();
                }

                var allRivalries = rivalries ?? [];
                var followedIds  = _followService.GetFollowedIds();

                foreach (var r in allRivalries)
                {
                    r.Team1IsFollowed = followedIds.Contains(r.Team1Id);
                    r.Team2IsFollowed = followedIds.Contains(r.Team2Id);
                    r.IsGameFavorited = _personalGameService.IsFavorited(r.Team1Id, r.Team2Id);
                }

                _allRivalries = [.. allRivalries
                    .OrderBy(r => TierSortOrder(r.RivalryTier))
                    .ThenBy(r => r.RivalryName)];

                await LoadPersonalGamesAsync(_allRivalries, followedIds);

                _selectedTier = "♥ Personal";
                OnPropertyChanged(nameof(SelectedTier));
                ApplyGamesFilter();

                StatusMessage = string.Empty;
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

        private async Task LoadPersonalGamesAsync(
            List<RivalryInfo> namedRivalries,
            HashSet<int> followedIds)
        {
            _personalGames.Clear();

            var namedKeys = namedRivalries
                .Select(r => PersonalGameService.Key(r.Team1Id, r.Team2Id))
                .ToHashSet();

            var personalKeys = _personalGameService.GetAll()
                .Where(k => !namedKeys.Contains(k))
                .ToList();

            if (personalKeys.Count == 0) return;

            var tasks = personalKeys.Select(async key =>
            {
                var (id1, id2) = PersonalGameService.ParseKey(key);
                var info       = await _apiService.GetMatchupHistoryAsync(id1, id2);
                if (info != null)
                {
                    info.Team1IsFollowed = followedIds.Contains(id1);
                    info.Team2IsFollowed = followedIds.Contains(id2);
                    info.IsGameFavorited = true;
                }
                return info;
            });

            var results = await Task.WhenAll(tasks);

            _personalGames = results
                .Where(r => r != null)
                .OrderBy(r => r!.Team1Name)
                .ToList()!;
        }

        // ── Filters ───────────────────────────────────────────────────────
        private void ApplyTeamFilter()
        {
            var filtered = _allTeams.AsEnumerable();

            // SelectedConference already stores the abbreviation — compare directly.
            // (The old DisplayToAbbr call treated it as a display name and, after the
            //  abbreviation refactor, silently filtered the Teams list to nothing.)
            var conf = _navState.SelectedConference;
            if (conf != "All")
            {
                filtered = filtered.Where(t =>
                    t.ConferenceAbbr != null &&
                    t.ConferenceAbbr.Equals(conf, StringComparison.OrdinalIgnoreCase));
            }

            var sorted = filtered
                .OrderByDescending(t => t.IsFollowed)
                .ThenBy(t => t.TeamName);

            Teams.Clear();
            foreach (var t in sorted)
                Teams.Add(t);
        }

        private void ApplyGamesFilter()
        {
            Games.Clear();

            IEnumerable<RivalryInfo> filtered;

            if (_selectedTier == "♥ Personal")
            {
                var namedPersonal = _allRivalries.Where(r => r.IsGameFavorited);
                filtered = namedPersonal.Concat(_personalGames)
                    .OrderBy(r => r.RivalryName ?? $"{r.Team1Name} vs {r.Team2Name}");
            }
            else
            {
                filtered = _selectedTier switch
                {
                    "🔥 Epic"     => _allRivalries.Where(r => r.RivalryTier == "EPIC"),
                    "⭐ National" => _allRivalries.Where(r => r.RivalryTier == "NATIONAL"),
                    "🏠 Regional" => _allRivalries.Where(r => r.RivalryTier == "STATE"),
                    "• Meh"       => _allRivalries.Where(r => r.RivalryTier == "MEH"),
                    _             => _allRivalries.AsEnumerable()
                };
            }

            foreach (var r in filtered)
                Games.Add(r);
        }

        // ── Event handlers ────────────────────────────────────────────────
        private void OnTeamFollowChanged(int teamId, bool isFollowed)
        {
            var team = _allTeams.FirstOrDefault(t => t.TeamID == teamId);
            if (team != null)
            {
                team.IsFollowed = isFollowed;
                ApplyTeamFilter();
            }

            foreach (var r in _allRivalries.Concat(_personalGames))
            {
                if (r.Team1Id == teamId) r.Team1IsFollowed = isFollowed;
                if (r.Team2Id == teamId) r.Team2IsFollowed = isFollowed;
            }
            ApplyGamesFilter();
        }

        private void OnGameFavoritedChange(string key, bool isFollowed)
        {
            var rivalry = _allRivalries.FirstOrDefault(r =>
                PersonalGameService.Key(r.Team1Id, r.Team2Id) == key);
            if (rivalry != null)
                rivalry.IsGameFavorited = isFollowed;

            var personalMatch = _personalGames.FirstOrDefault(r =>
                PersonalGameService.Key(r.Team1Id, r.Team2Id) == key);

            if (isFollowed && rivalry == null && personalMatch == null)
            {
                _ = LoadDataAsync();
                return;
            }

            if (!isFollowed && personalMatch != null)
                _personalGames.Remove(personalMatch);

            if (_selectedTier == "♥ Personal")
                ApplyGamesFilter();
        }

        private static int TierSortOrder(string? tier) => tier switch
        {
            "EPIC"     => 0,
            "NATIONAL" => 1,
            "STATE"    => 2,
            "MEH"      => 3,
            _          => 4
        };
    }
}
