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

        public bool IsFollowingExpanded    => _expandedSection == "Following";
        public bool IsUserConfigExpanded   => _expandedSection == "UserConfig";
        public bool IsMoreCoolStuffExpanded => _expandedSection == "MoreCoolStuff";

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
        // Passthrough to SharedNavigationStateService so the setting
        // propagates to all tabs automatically via PropertyChanged.

        public bool ShowFavoritesFirst
        {
            get => _navState.ShowFavoritesFirst;
            set => _navState.ShowFavoritesFirst = value;
        }

        // ── Teams ─────────────────────────────────────────────────────────
        public ObservableCollection<TeamInfo> Teams             { get; } = new();
        public ObservableCollection<string>   ConferenceFilters { get; } = new();

        private string _selectedConference = "All";
        public string SelectedConference
        {
            get => _selectedConference;
            set
            {
                if (_selectedConference == value) return;
                _selectedConference = value;
                OnPropertyChanged();
                ApplyTeamFilter();
            }
        }

        // ── Games ─────────────────────────────────────────────────────────
        public ObservableCollection<RivalryInfo> Games { get; } = new();

        // TierFilters is an ObservableCollection so the Picker binds
        // before we set SelectedTier, avoiding the blank-until-expand bug
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

        // ── Commands ──────────────────────────────────────────────────────
        public ICommand LoadDataCommand       { get; }
        public ICommand SelectViewCommand     { get; }
        public ICommand TogglePersonalCommand { get; }
        public ICommand ToggleSectionCommand  { get; }
        public ICommand ToggleFollowCommand   { get; }

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

            // Populate TierFilters here so the ObservableCollection
            // is ready before any binding occurs
            TierFilters.Add("All");
            TierFilters.Add("♥ Personal");
            TierFilters.Add("── Rivalries ──");
            TierFilters.Add("🔥 Epic");
            TierFilters.Add("⭐ National");
            TierFilters.Add("🏠 Regional");
            TierFilters.Add("• Meh");

            LoadDataCommand = new Microsoft.Maui.Controls.Command(
                async () => await LoadDataAsync());

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

            ToggleSectionCommand = new Command<string>(section =>
            {
                _expandedSection = _expandedSection == section ? null : section;
                OnPropertyChanged(nameof(IsFollowingExpanded));
                OnPropertyChanged(nameof(IsUserConfigExpanded));
                OnPropertyChanged(nameof(IsMoreCoolStuffExpanded));
            });

            ToggleFollowCommand = new Microsoft.Maui.Controls.Command<int>(teamId =>
            {
                _followService.Toggle(teamId);
            });

            _followService.TeamFollowChanged         += OnTeamFollowChanged;
            _personalGameService.GameFavoritedChange += OnGameFavoritedChange;
        }

        // ── Load ──────────────────────────────────────────────────────────
        public async Task LoadDataAsync()
        {
            if (IsBusy) return;
            IsBusy = true;
            StatusMessage = "Loading...";

            try
            {
                var teamsTask     = _apiService.GetTeamsAsync();
                var rivalriesTask = _apiService.GetNamedRivalriesAsync();

                await Task.WhenAll(teamsTask, rivalriesTask);

                // ── Teams ─────────────────────────────────────────────────
                var teams = teamsTask.Result;
                if (teams != null && teams.Count > 0)
                {
                    foreach (var t in teams)
                        t.IsFollowed = _followService.IsFollowed(t.TeamID);

                    _allTeams = [.. teams.OrderBy(t => t.TeamName)];

                    ConferenceFilters.Clear();
                    foreach (var c in ConferenceHelper.FilterDisplayList())
                        ConferenceFilters.Add(c);

                    _selectedConference = "All";
                    OnPropertyChanged(nameof(SelectedConference));
                    ApplyTeamFilter();
                }

                // ── Named rivalries ───────────────────────────────────────
                var rivalries   = rivalriesTask.Result ?? [];
                var followedIds = _followService.GetFollowedIds();

                foreach (var r in rivalries)
                {
                    r.Team1IsFollowed = followedIds.Contains(r.Team1Id);
                    r.Team2IsFollowed = followedIds.Contains(r.Team2Id);
                    r.IsGameFavorited = _personalGameService.IsFavorited(r.Team1Id, r.Team2Id);
                }

                _allRivalries = [.. rivalries
                    .OrderBy(r => TierSortOrder(r.RivalryTier))
                    .ThenBy(r => r.RivalryName)];

                // ── Personal games (non-rivalry matchups from Scores) ─────
                await LoadPersonalGamesAsync(_allRivalries, followedIds);

                // Set default tier AFTER data is ready so Picker shows correctly
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
                var info = await _apiService.GetMatchupHistoryAsync(id1, id2);
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

            if (SelectedConference != "All")
            {
                var abbr = ConferenceHelper.DisplayToAbbr(SelectedConference);
                filtered = filtered.Where(t => t.ConferenceAbbr == abbr);
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
                    "🏠 Regional" => _allRivalries.Where(r => r.RivalryTier == "REGIONAL"),
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
            "REGIONAL" => 2,
            "MEH"      => 3,
            _          => 4
        };
    }
}
