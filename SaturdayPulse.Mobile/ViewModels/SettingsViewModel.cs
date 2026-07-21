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
        private readonly TeamCacheService              _teamCache;
        private readonly UserApiService                _userApi;
        private readonly AuthService                   _authService;

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
        // User Profile is the default-expanded section (was "nothing" before
        // the Auth0 login/Season Pass controls landed there).
        private string? _expandedSection = "UserProfile";

        public bool IsFollowingExpanded     => _expandedSection == "Following";
        public bool IsUserConfigExpanded    => _expandedSection == "UserConfig";
        public bool IsUserProfileExpanded   => _expandedSection == "UserProfile";
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

        // ── User preference: Default team (My Teams' primary team) ────────
        // Lives on FollowService rather than SharedNavigationStateService —
        // it's a team-follow concept (see FollowService.SetPrimaryTeam),
        // not a game-data filter like DefaultWeek/DefaultConference.
        public string DefaultTeamDisplay =>
            _followService.GetPrimaryTeamId() is int id
                ? _teamCache.GetTeam(id)?.TeamName ?? "None"
                : "None";

        // ── User preference: Default landing page ──────────────────────────
        // Standalone app-level preference (which tab MainPage.xaml.cs shows
        // at startup — see GetInitialTabIndex there). Not part of
        // SharedNavigationStateService since it's navigation UI state, not
        // game-data filter state.
        private const string DefaultLandingPageKey = "DefaultLandingPage";

        public string DefaultLandingPage
        {
            get => Preferences.Default.Get(DefaultLandingPageKey, "MyTeams");
            set
            {
                Preferences.Default.Set(DefaultLandingPageKey, value);
                OnPropertyChanged();
                OnPropertyChanged(nameof(DefaultLandingPageDisplay));
            }
        }

        public string DefaultLandingPageDisplay => DefaultLandingPage switch
        {
            "MyTeams"    => "My Teams",
            "Scores"     => "Scores",
            "Rankings"   => "Rankings",
            "Postseason" => "Postseason",
            "Sandbox"    => "Sandbox",
            "Settings"   => "Settings",
            _            => "My Teams"
        };

        // ── User preference: Handle ─────────────────────────────────────
        // Sourced from UserProfile via UserApiService — no local Preferences
        // copy. Populated by LoadDataAsync alongside teams/rivalries.
        private string _handle = string.Empty;

        public string Handle
        {
            get => _handle;
            private set
            {
                _handle = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsDefaultHandle));
            }
        }

        /// <summary>
        /// True while the handle still matches the server's auto-generated
        /// "user_{shortguid}" default pattern — i.e. the person has never
        /// picked one. Drives the first-launch routing in MainPage.xaml.cs.
        /// </summary>
        public bool IsDefaultHandle => Handle.StartsWith("user_", StringComparison.OrdinalIgnoreCase);

        private string _email = string.Empty;
        public string Email
        {
            get => _email;
            private set { _email = value; OnPropertyChanged(); OnPropertyChanged(nameof(EmailDisplay)); }
        }

        public string EmailDisplay => string.IsNullOrEmpty(Email) ? "Not set" : Email;

        private string _phoneNumber = string.Empty;
        public string PhoneNumber
        {
            get => _phoneNumber;
            private set { _phoneNumber = value; OnPropertyChanged(); OnPropertyChanged(nameof(PhoneDisplay)); }
        }

        public string PhoneDisplay => string.IsNullOrEmpty(PhoneNumber) ? "Not set" : PhoneNumber;

        private bool _marketingSmsConsent;
        public bool MarketingSmsConsent
        {
            get => _marketingSmsConsent;
            private set { _marketingSmsConsent = value; OnPropertyChanged(); }
        }

        // ── Auth0 — login state / Season Pass ───────────────────────────
        // No forced login anywhere in the app. This is purely opt-in: the
        // person taps Login/Create Account (or Season Pass, which offers to
        // log in first) when THEY want to. See AuthService for StayLoggedIn.

        private bool _isLoggedIn;
        public bool IsLoggedIn
        {
            get => _isLoggedIn;
            private set
            {
                if (_isLoggedIn == value) return;
                _isLoggedIn = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(AuthButtonText));
            }
        }

        /// <summary>
        /// "Logout" if there's an active session; otherwise "Login" if this
        /// device has logged in before, or "Create Account" if it hasn't —
        /// AuthService.HasAccount is the local (device-level) signal for that,
        /// there's no server round trip to determine it.
        /// </summary>
        public string AuthButtonText =>
            IsLoggedIn ? "Logout" : (_authService.HasAccount ? "Login" : "Create Account");

        public bool StayLoggedIn
        {
            get => _authService.StayLoggedIn;
            set { _authService.StayLoggedIn = value; OnPropertyChanged(); }
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
        public ICommand SelectDefaultTeamCommand         { get; }
        public ICommand SelectDefaultLandingPageCommand  { get; }
        public ICommand EditHandleCommand              { get; }
        public ICommand EditEmailCommand               { get; }
        public ICommand EditPhoneCommand               { get; }
        public ICommand ClearLogCommand                { get; }
        public ICommand AuthActionCommand              { get; }
        public ICommand SeasonPassCommand              { get; }
        public ICommand CloseCommand                   { get; }

        /// <summary>
        /// Raised when the Close link is tapped. Settings' BindingContext is
        /// this ViewModel, not MainViewModel (see AddPageToHost in
        /// MainPage.xaml.cs), so the tab-switch logic that actually closes
        /// Settings can't be reached via a XAML binding — MainPage.xaml.cs
        /// subscribes to this event and forwards to
        /// MainViewModel.CloseSettingsCommand.
        /// </summary>
        public event EventHandler? CloseRequested;

        // ── Constructor ───────────────────────────────────────────────────
        public SettingsViewModel(
            GameDataApiService apiService,
            FollowService followService,
            PersonalGameService personalGameService,
            SharedNavigationStateService navState,
            TeamCacheService teamCache,
            UserApiService userApi,
            AuthService authService)
            : base(followService)
        {
            _apiService          = apiService;
            _personalGameService = personalGameService;
            _navState            = navState;
            _teamCache           = teamCache;
            _userApi             = userApi;
            _authService         = authService;

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
                OnPropertyChanged(nameof(IsUserProfileExpanded));
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

            SelectDefaultTeamCommand = new Microsoft.Maui.Controls.Command(async () =>
            {
                // Settings may be opened before My Teams has ever loaded —
                // make sure the team list is warm before building the sheet.
                await _teamCache.EnsureLoadedAsync();

                var options = new List<string> { "None" };
                options.AddRange(_teamCache.Teams.OrderBy(t => t.TeamName).Select(t => t.TeamName));

                var result = await Shell.Current.DisplayActionSheet(
                    "Default Team", "Cancel", null, options.ToArray());

                if (result == null || result == "Cancel") return;

                if (result == "None")
                {
                    _followService.SetPrimaryTeam(null);
                }
                else
                {
                    var team = _teamCache.Teams.FirstOrDefault(t => t.TeamName == result);
                    if (team != null)
                        _followService.SetPrimaryTeam(team.TeamID);
                }

                OnPropertyChanged(nameof(DefaultTeamDisplay));
            });

            SelectDefaultLandingPageCommand = new Microsoft.Maui.Controls.Command(async () =>
            {
                var options = new[] { "My Teams", "Scores", "Rankings", "Postseason", "Sandbox", "Settings" };

                var result = await Shell.Current.DisplayActionSheet(
                    "Default Landing Page", "Cancel", null, options);

                if (result == null || result == "Cancel") return;

                // Maps display label -> the same string keys GetInitialTabIndex
                // in MainPage.xaml.cs switches on.
                DefaultLandingPage = result switch
                {
                    "My Teams"   => "MyTeams",
                    "Scores"     => "Scores",
                    "Rankings"   => "Rankings",
                    "Postseason" => "Postseason",
                    "Sandbox"    => "Sandbox",
                    "Settings"   => "Settings",
                    _            => "MyTeams"
                };
            });

            // Prompts for a new handle, PATCHes it, and only updates the
            // bound Handle (and therefore IsDefaultHandle) on success — a
            // failed/duplicate-handle response leaves the displayed value
            // untouched rather than showing something that didn't save.
            EditHandleCommand = new Microsoft.Maui.Controls.Command(async () =>
            {
                var result = await Shell.Current.DisplayPromptAsync(
                    "Handle", "Choose a display handle", initialValue: Handle, maxLength: 32);

                if (string.IsNullOrWhiteSpace(result) || result.Trim() == Handle) return;

                var trimmed = result.Trim();
                var ok = await _userApi.UpdateHandleAsync(trimmed);
                if (ok)
                    Handle = trimmed;
                else
                    StatusMessage = "Couldn't update handle — it may already be taken.";
            });

            EditEmailCommand = new Microsoft.Maui.Controls.Command(async () =>
            {
                var result = await Shell.Current.DisplayPromptAsync(
                    "Email", "Enter your email address",
                    initialValue: Email, keyboard: Keyboard.Email, maxLength: 254);

                if (string.IsNullOrWhiteSpace(result) || result.Trim() == Email) return;

                var trimmed = result.Trim();
                var ok = await _userApi.UpdateEmailAsync(trimmed);
                if (ok)
                    Email = trimmed;
                else
                    StatusMessage = "Couldn't update email — it may already be in use.";
            });

            // The phone endpoint bundles marketing SMS consent into the same
            // PATCH as the number itself (see UserController — there's no
            // separate consent-only endpoint), so this asks both in one flow
            // rather than exposing consent as a standalone, always-visible
            // toggle that would have nothing to save on its own.
            EditPhoneCommand = new Microsoft.Maui.Controls.Command(async () =>
            {
                var result = await Shell.Current.DisplayPromptAsync(
                    "Phone Number", "Enter your phone number",
                    initialValue: PhoneNumber, keyboard: Keyboard.Telephone, maxLength: 20);

                if (string.IsNullOrWhiteSpace(result)) return;
                var trimmed = result.Trim();

                var consent = await Shell.Current.DisplayAlert(
                    "Text Alerts", "OK to text you game and score alerts at this number?", "Yes", "No");

                var ok = await _userApi.UpdatePhoneAsync(trimmed, consent);
                if (ok)
                {
                    PhoneNumber = trimmed;
                    MarketingSmsConsent = consent;
                }
                else
                {
                    StatusMessage = "Couldn't update phone number.";
                }
            });

            // Single button whose label (AuthButtonText) already reflects
            // what tapping it will do — Create Account and Login both just
            // call AuthService.LoginAsync (isSignup only changes which tab
            // Auth0's hosted page opens on), Logout calls LogoutAsync.
            AuthActionCommand = new Microsoft.Maui.Controls.Command(async () =>
            {
                if (IsLoggedIn)
                {
                    await _authService.LogoutAsync();
                    IsLoggedIn = false;
                    return;
                }

                var isSignup = !_authService.HasAccount;
                var ok = await _authService.LoginAsync(isSignup);
                if (ok)
                    IsLoggedIn = true;
                else
                    StatusMessage = "Login failed — try again.";
            });

            // Placeholder — Stripe isn't wired up yet (separate feature).
            // The only real behavior here is the login check: buying a
            // Season Pass should be tied to a real identity, so if nobody's
            // logged in this offers to log in first rather than letting the
            // "purchase" proceed anonymously. Nothing else needs validating
            // since there's no other purchase path yet.
            SeasonPassCommand = new Microsoft.Maui.Controls.Command(async () =>
            {
                if (!IsLoggedIn)
                {
                    var proceed = await Shell.Current.DisplayAlert(
                        "Season Pass",
                        "You'll need to log in first to purchase a Season Pass.",
                        "Log In", "Cancel");
                    if (!proceed) return;

                    var isSignup = !_authService.HasAccount;
                    var ok = await _authService.LoginAsync(isSignup);
                    if (!ok)
                    {
                        StatusMessage = "Login failed — try again.";
                        return;
                    }
                    IsLoggedIn = true;
                }

                await Shell.Current.DisplayAlert(
                    "Season Pass", "Coming soon — payment isn't wired up yet.", "OK");
            });

            CloseCommand = new Microsoft.Maui.Controls.Command(() =>
                CloseRequested?.Invoke(this, EventArgs.Empty));

            ClearLogCommand = new Microsoft.Maui.Controls.Command(() =>
            {
                AppLogger.Clear();
                OnPropertyChanged(nameof(LogEntryCount));
            });

            // Keep LogEntryCount in sync as entries are added/removed
            AppLogger.Entries.CollectionChanged += (s, e) =>
                OnPropertyChanged(nameof(LogEntryCount));

            _followService.TeamFollowChanged         += OnTeamFollowChanged;
            _followService.PrimaryTeamChanged        += _ => OnPropertyChanged(nameof(DefaultTeamDisplay));
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
                var (teams, rivalries, profile) = await Task.Run(async () =>
                {
                    var teamsTask     = _apiService.GetTeamsAsync();
                    var rivalriesTask = _apiService.GetNamedRivalriesAsync();
                    var profileTask   = _userApi.GetMeAsync();
                    await Task.WhenAll(teamsTask, rivalriesTask, profileTask);
                    return (teamsTask.Result, rivalriesTask.Result, profileTask.Result);
                });

                if (teams != null && teams.Count > 0)
                {
                    foreach (var t in teams)
                        t.IsFollowed = _followService.IsFollowed(t.TeamID);

                    _allTeams = [.. teams.OrderBy(t => t.TeamName)];
                    ApplyTeamFilter();
                }

                if (profile != null)
                {
                    Handle = profile.Handle;
                    Email = profile.Email ?? string.Empty;
                    PhoneNumber = profile.PhoneNumber ?? string.Empty;
                    MarketingSmsConsent = profile.MarketingSmsConsent ?? false;
                }

                IsLoggedIn = await _authService.IsAuthenticatedAsync();

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
