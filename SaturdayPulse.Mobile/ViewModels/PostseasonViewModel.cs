using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SaturdayPulse.Helpers;
using SaturdayPulse.Models;
using SaturdayPulse.Services;

namespace SaturdayPulse.ViewModels
{
    /// <summary>
    /// Drives the Postseason page — Title Games, Playoffs, and Bowls tabs.
    /// Title Games come from the championship qualifiers endpoint.
    /// Playoffs and Bowls are filtered out of the shared schedule cache.
    /// </summary>
    public class PostseasonViewModel : BaseViewModel
    {
        private readonly GameDataApiService           _apiService;
        private readonly GameDataCacheService         _cache;
        private readonly SharedNavigationStateService _navState;

        private List<ChampionshipMatchup> _allChampionships = new();
        private bool   _isBusy;
        private string _selectedView  = "Championship";
        private string _statusMessage = "Loading...";
        private string _emptyMessage = "Loading...";

        public PostseasonViewModel(
            GameDataApiService apiService,
            GameDataCacheService cache,
            FollowService followService,
            SharedNavigationStateService navState)
            : base(followService)
        {
            _apiService = apiService;
            _cache      = cache;
            _navState   = navState;

            // No outer Task.Run — LoadDataAsync runs on the main thread; the HTTP
            // calls inside it are offloaded via their own Task.Run, and the
            // continuations (ApplyConferenceFilter / RebuildPostseasonFromCache)
            // return to the main thread.
            LoadDataCommand = new Microsoft.Maui.Controls.Command(() => _ = LoadDataAsync());
            RefreshCommand  = new Microsoft.Maui.Controls.Command(() => _ = LoadDataAsync(forceReload: true));

            SelectViewCommand = new Microsoft.Maui.Controls.Command<string>(view =>
            {
                SelectedView = view;
            });

            ToggleMatchupExpandCommand = new Microsoft.Maui.Controls.Command<ChampionshipMatchup>(matchup =>
            {
                if (matchup != null) matchup.IsExpanded = !matchup.IsExpanded;
            });

            ToggleContendersExpandCommand = new Microsoft.Maui.Controls.Command<ChampionshipMatchup>(matchup =>
            {
                if (matchup != null) matchup.IsContendersExpanded = !matchup.IsContendersExpanded;
            });

            // Title Games "Details" toggle. One command for the one toggle slot
            // in the shared row — routes to whichever card is actually active
            // (real Game vs. Sandbox) rather than having two separate toggles
            // fight over the same spot.
            ToggleTitleGameDetailsCommand = new Microsoft.Maui.Controls.Command<ChampionshipMatchup>(matchup =>
            {
                if (matchup == null) return;

                if (matchup.HasGame)
                {
                    if (matchup.Game != null)
                        matchup.Game.IsDetailsExpanded = !matchup.Game.IsDetailsExpanded;
                }
                else
                {
                    matchup.IsSandboxDetailsExpanded = !matchup.IsSandboxDetailsExpanded;
                }
            });

            ToggleDetailsCommand = new Microsoft.Maui.Controls.Command<GameResult>(game =>
            {
                if (game == null) return;
                game.IsDetailsExpanded = !game.IsDetailsExpanded;
            });

            // Manual single-game refresh (⟳ icon, Season-Pass-gated in XAML).
            // Mirrors ScheduleViewModel's RefreshGameCommand exactly — same
            // endpoint, same guard, same failure UX. Kept independent per-VM
            // rather than hoisted into BaseViewModel to match the existing
            // ToggleDetailsCommand duplication pattern already in this codebase.
            RefreshGameCommand = new Microsoft.Maui.Controls.Command<GameResult>(async game =>
            {
                if (game == null || game.IsRefreshing) return;

                game.IsRefreshing = true;
                try
                {
                    var result = await _apiService.RefreshGameAsync(game.Id);
                    if (result == null)
                    {
                        await Shell.Current.DisplayAlert(
                            "Refresh Failed", "Couldn't refresh this game. Try again in a moment.", "OK");
                        return;
                    }

                    game.HomePoints = result.HomePoints;
                    game.AwayPoints = result.AwayPoints;
                }
                finally
                {
                    game.IsRefreshing = false;
                }
            });

            // Section collapse toggles
            ToggleRoundExpandCommand = new Microsoft.Maui.Controls.Command<PlayoffRound>(round =>
            {
                if (round != null) round.IsExpanded = !round.IsExpanded;
            });

            ToggleWeekendExpandCommand = new Microsoft.Maui.Controls.Command<BowlWeekendGroup>(weekend =>
            {
                if (weekend != null) weekend.IsExpanded = !weekend.IsExpanded;
            });

            _navState.PropertyChanged += OnNavStateChanged;
            _cache.CacheUpdated       += OnCacheUpdated;
        }

        // ── Bindable collections ──────────────────────────────────────────

        public ObservableCollection<ChampionshipMatchup> Championships { get; } = new();
        public ObservableCollection<PlayoffRound>        PlayoffRounds { get; } = new();
        public ObservableCollection<BowlWeekendGroup>    BowlWeekends  { get; } = new();

        // ── Bindable properties ───────────────────────────────────────────

        public bool IsBusy
        {
            get => _isBusy;
            set { _isBusy = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsLoading)); }
        }

        public bool IsLoading       => _isBusy;
        public bool HasLoaded       { get; set; }

        /// <summary>
        /// True only while Postseason is the visible tab (set by MainPage on tab
        /// switch). When false, the page defers FilterChanged work instead of
        /// loading off-screen; the lazy SyncPage path loads it on first visit.
        /// </summary>
        public bool IsActive        { get; set; }
        public bool HasPlayoffData  => PlayoffRounds.Any();
        public bool HasBowlData     => BowlWeekends.Any();

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

        public string SelectedView
        {
            get => _selectedView;
            set
            {
                _selectedView = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsChampionshipView));
                OnPropertyChanged(nameof(IsPlayoffsView));
                OnPropertyChanged(nameof(IsBowlsView));
            }
        }

        public bool IsChampionshipView => _selectedView == "Championship";
        public bool IsPlayoffsView     => _selectedView == "Playoffs";
        public bool IsBowlsView        => _selectedView == "Bowls";

        // ── Commands ──────────────────────────────────────────────────────

        public ICommand LoadDataCommand               { get; }
        public ICommand RefreshCommand                { get; }
        public ICommand SelectViewCommand             { get; }
        public ICommand ToggleMatchupExpandCommand    { get; }
        public ICommand ToggleContendersExpandCommand { get; }
        public ICommand ToggleTitleGameDetailsCommand  { get; }
        public ICommand ToggleDetailsCommand          { get; }
        public ICommand RefreshGameCommand            { get; }
        public ICommand ToggleRoundExpandCommand      { get; }
        public ICommand ToggleWeekendExpandCommand    { get; }

        // ── Load ──────────────────────────────────────────────────────────

        public async Task LoadDataAsync(bool forceReload = false)
        {
            if (IsBusy) return;
            IsBusy = true;
            StatusMessage = "Loading...";

            try
            {
                // Games cache first — AttachRealGames below depends on _cache.AllGames
                // already reflecting the selected year.
                await Task.Run(async () =>
                    await _cache.GetGamesForYearAsync(_navState.SelectedYear, forceReload));

                if (_navState.SelectedYear >= 2016)
                {
                    var championships = await Task.Run(async () =>
                        await _apiService.GetProjectedChampionshipQualifiersAsync(
                            _navState.SelectedYear, _navState.SelectedWeek));
                    if (championships != null)
                    {
                        _allChampionships = championships;

                        // Real Games data takes priority. The qualifiers endpoint's
                        // own Game field isn't reliably populated even once CFBD has
                        // published the actual game (confirmed 2025 Wk15 SEC — real
                        // result existed in Games/_cache.AllGames but Game stayed
                        // null here) — so match against the schedule cache directly
                        // instead of trusting that field. Only matchups still missing
                        // a Game after this get a Sandbox projection.
                        AttachRealGames(_allChampionships, _cache.AllGames, _navState.SelectedYear, _navState.SelectedWeek);

                        await Task.Run(async () =>
                            await EnrichChampionshipsWithPredictionsAsync(
                                _allChampionships, _navState.SelectedYear));

                        ApplyConferenceFilter();
                    }
                }
                else
                {
                    _allChampionships.Clear();
                    Championships.Clear();
                }

                RebuildPostseasonFromCache();

                StatusMessage = $"{_navState.SelectedYear} projections";
                HasLoaded = true;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to load game data. Error: {ex.Message}";
                EmptyMessage = "Failed to load game data.";
            }
            finally
            {
                IsBusy = false;
            }
        }

        // ── Title Games: attach a real Game from the schedule cache when one exists ──

        private void AttachRealGames(
            List<ChampionshipMatchup> championships, IReadOnlyList<GameResult>? allGames,
            int year, int selectedWeek)
        {
            if (allGames == null || allGames.Count == 0) return;

            var championshipWeek = DetermineChampionshipWeek(year, allGames);
            if (championshipWeek == null)
            {
                // Can't reliably place championship week yet (schedule cache
                // doesn't extend that far into the season) — leave every
                // matchup on the Sandbox path rather than risk matching a
                // regular-season meeting as if it were the title game.
                System.Diagnostics.Debug.WriteLine(
                    $"[Postseason] Could not determine championship week for {year} " +
                    "from loaded schedule — all matchups stay on Sandbox projection.");
                return;
            }

            // "Week" on this page is an as-of scrubber, same convention as
            // KickoffTime/IsPlayed elsewhere in the app — even when the real
            // Games row already exists (including for an entire completed
            // past season, where every week's data technically exists in the
            // DB already), don't surface it until the selected week has
            // actually reached championship week. Otherwise browsing an
            // earlier week of a finished season would show the real result
            // instead of what would have been known at that point in time.
            if (selectedWeek < championshipWeek.Value)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[Postseason] Selected week {selectedWeek} is before championship " +
                    $"week {championshipWeek.Value} for {year} — staying on Sandbox projection.");
                return;
            }

            foreach (var matchup in championships)
            {
                if (matchup.Game != null) continue; // API already gave us one
                matchup.Game = FindRealGame(matchup, allGames, championshipWeek.Value);
            }
        }

        /// <summary>
        /// Matches a championship matchup's two qualifiers against the schedule
        /// cache by team name, restricted to the actual championship week. The
        /// same two teams can legitimately meet twice in a season (an earlier
        /// regular-season game plus the championship) — without the week
        /// restriction, a same-team-pair regular-season game gets mistaken for
        /// the title game whenever the real championship row doesn't exist in
        /// the Games table yet (confirmed 2026 Wk13 SEC: Georgia/South Carolina's
        /// Week 12 regular-season meeting was wrongly shown as the title game).
        /// </summary>
        private static GameResult? FindRealGame(
            ChampionshipMatchup matchup, IEnumerable<GameResult> allGames, int championshipWeek)
        {
            if (matchup.Qualifier1 == null || matchup.Qualifier2 == null) return null;

            var team1 = matchup.Qualifier1.TeamName;
            var team2 = matchup.Qualifier2.TeamName;

            return allGames.FirstOrDefault(g =>
                g.Week == championshipWeek &&
                ((g.HomeName.Equals(team1, StringComparison.OrdinalIgnoreCase) &&
                  g.AwayName.Equals(team2, StringComparison.OrdinalIgnoreCase)) ||
                 (g.HomeName.Equals(team2, StringComparison.OrdinalIgnoreCase) &&
                  g.AwayName.Equals(team1, StringComparison.OrdinalIgnoreCase))));
        }

        /// <summary>
        /// Conference championship weekend is the Saturday one week after
        /// Thanksgiving weekend (Thanksgiving = 4th Thursday of November;
        /// rivalry-week Saturday = Thanksgiving + 2 days; championship
        /// Saturday = rivalry Saturday + 7 days). This app has no direct
        /// calendar-date-to-internal-Week mapping, so the target date is
        /// matched against the median GameDate of each Week already present
        /// in the loaded schedule cache, picking the closest. Returns null
        /// (rather than a wrong guess) if the closest match is still more
        /// than 10 days from the target — i.e. the season isn't loaded far
        /// enough yet to know.
        /// </summary>
        private static int? DetermineChampionshipWeek(int year, IReadOnlyList<GameResult> allGames)
        {
            var thanksgiving = NthWeekdayOfMonth(year, month: 11, DayOfWeek.Thursday, occurrence: 4);
            var championshipSaturday = thanksgiving.AddDays(9);

            var weekDates = allGames
                .Select(g => new { g.Week, Date = g.GameDate.ToDateTime() })
                .Where(x => x.Date.HasValue)
                .GroupBy(x => x.Week)
                .Select(g =>
                {
                    var ordered = g.OrderBy(x => x.Date!.Value).ToList();
                    return new { Week = g.Key, MedianDate = ordered[ordered.Count / 2].Date!.Value };
                })
                .ToList();

            if (weekDates.Count == 0) return null;

            var closest = weekDates
                .OrderBy(w => Math.Abs((w.MedianDate - championshipSaturday).TotalDays))
                .First();

            return Math.Abs((closest.MedianDate - championshipSaturday).TotalDays) <= 10
                ? closest.Week
                : (int?)null;
        }

        /// <summary>The Nth occurrence of a weekday in a given month/year (e.g. 4th Thursday of November = Thanksgiving).</summary>
        private static DateTime NthWeekdayOfMonth(int year, int month, DayOfWeek dayOfWeek, int occurrence)
        {
            var first = new DateTime(year, month, 1);
            int offset = ((int)dayOfWeek - (int)first.DayOfWeek + 7) % 7;
            return first.AddDays(offset + (occurrence - 1) * 7);
        }

        // ── Title Games: Sandbox-projection fallback for matchups with no real Game ──

        private async Task EnrichChampionshipsWithPredictionsAsync(
            List<ChampionshipMatchup> championships, int year)
        {
            var needsPrediction = championships.Where(c => c.NeedsPrediction).ToList();
            if (needsPrediction.Count == 0) return;

            // One shared power-rankings fetch for the year instead of one per team
            // per matchup — Qualifier.TeamName always matches TeamRanking.TeamName
            // exactly (both source from CFBD; a mismatch is a CFBD data issue, not
            // something to paper over here).
            List<TeamRanking>? rankings = null;
            try
            {
                // Passing null here originally routed the API to the season-aggregate
                // TeamRecords table, which has NULL AvgPointsScored for a chunk of
                // teams at week 1 and throws a JsonException on deserialization.
                // Passing an actual week routes to WeeklyRankings instead — same
                // table the championship-qualifiers call above already uses
                // successfully, populated with seed/projected values from week 1.
                rankings = await _apiService.GetPowerRankingsAsync(year, _navState.SelectedWeek);
            }
            catch (Exception ex)
            {
                // Rankings are a nice-to-have on the Details panel — fall through
                // with rankings == null so the prediction rows still render, but
                // log the real cause instead of swallowing it silently.
                System.Diagnostics.Debug.WriteLine(
                    $"[Postseason] GetPowerRankingsAsync({year}, {_navState.SelectedWeek}) failed: {ex}");
            }

            System.Diagnostics.Debug.WriteLine(
                $"[Postseason] GetPowerRankingsAsync({year}, {_navState.SelectedWeek}) returned {rankings?.Count ?? -1} rankings.");

            var rankingsByName = (rankings ?? new List<TeamRanking>())
                .GroupBy(r => r.TeamName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            // Rankings assigned synchronously first (cheap, no I/O), then every
            // Sandbox prediction is fetched in parallel rather than one matchup
            // at a time — a serial chain across several conferences could take
            // long enough that a fast week-to-week tap lands while IsBusy is
            // still true and gets silently dropped by LoadDataAsync's guard.
            var predictionTasks = new List<Task>();

            foreach (var matchup in needsPrediction)
            {
                if (matchup.Qualifier1 == null || matchup.Qualifier2 == null) continue;

                rankingsByName.TryGetValue(matchup.Qualifier1.TeamName, out var rank1);
                rankingsByName.TryGetValue(matchup.Qualifier2.TeamName, out var rank2);
                matchup.Qualifier1Ranking = rank1;
                matchup.Qualifier2Ranking = rank2;

                if (rank1 == null)
                    System.Diagnostics.Debug.WriteLine(
                        $"[Postseason] No TeamRanking match for '{matchup.Qualifier1.TeamName}' ({matchup.Conference}).");
                if (rank2 == null)
                    System.Diagnostics.Debug.WriteLine(
                        $"[Postseason] No TeamRanking match for '{matchup.Qualifier2.TeamName}' ({matchup.Conference}).");

                predictionTasks.Add(FetchSandboxPredictionAsync(matchup, year));
            }

            await Task.WhenAll(predictionTasks);
        }

        private async Task FetchSandboxPredictionAsync(ChampionshipMatchup matchup, int year)
        {
            try
            {
                matchup.Prediction = await _apiService.GetSandboxPredictionAsync(
                    matchup.Qualifier1.TeamName, year,
                    matchup.Qualifier2.TeamName, year);
            }
            catch (Exception ex)
            {
                // A failed prediction just leaves the Sandbox block empty
                // (HasPrediction stays false) rather than blocking the rest
                // of the Title Games load.
                System.Diagnostics.Debug.WriteLine(
                    $"[Postseason] GetSandboxPredictionAsync failed for " +
                    $"{matchup.Qualifier1.TeamName} vs {matchup.Qualifier2.TeamName}: {ex.Message}");
                matchup.Prediction = null;
            }
        }

        // ── Build Bowls + Playoffs from cached schedule ───────────────────

        private void RebuildPostseasonFromCache()
        {
            var allGames = _cache.AllGames;
            if (allGames == null || allGames.Count == 0)
            {
                PlayoffRounds.Clear();
                BowlWeekends.Clear();
                OnPropertyChanged(nameof(HasPlayoffData));
                OnPropertyChanged(nameof(HasBowlData));
                return;
            }

            // ── Playoffs ──────────────────────────────────────────────────
            var weekToRound = new Dictionary<int, string>
            {
                { 17, "First Round" },
                { 19, "Quarterfinals" },
                { 20, "Semifinals" },
                { 21, "National Championship" }
            };

            var playoffRounds = allGames
                .Where(g => g.SeasonType == "playoff" && g.Year >= 2014)
                .GroupBy(g => g.Week)
                .OrderBy(g => g.Key)
                .Select(weekGrp =>
                {
                    var label = weekToRound.TryGetValue(weekGrp.Key, out var r)
                                    ? r : $"Week {weekGrp.Key}";
                    var days = weekGrp
                        .GroupBy(g => g.GroupHeader)
                        .OrderBy(g => g.Key)
                        .Select(dayGrp => new PlayoffDayGroup(dayGrp.Key, dayGrp.ToList()))
                        .ToList();
                    return new PlayoffRound(label, days);
                })
                .ToList();

            PlayoffRounds.Clear();
            foreach (var round in playoffRounds)
                PlayoffRounds.Add(round);
            OnPropertyChanged(nameof(HasPlayoffData));

            // ── Bowls — grouped by weekend (Fri–Sun), then by day ─────────
            // SelectedConference now stores Abbreviation directly — no DisplayToAbbr needed
            var conf     = _navState.SelectedConference;
            var confAbbr = conf == "All" ? null : conf;

            var bowlGames = allGames.Where(g => g.SeasonType == "postseason");

            if (confAbbr != null)
            {
                bowlGames = bowlGames.Where(g =>
                    g.HomeConf.Equals(confAbbr, StringComparison.OrdinalIgnoreCase) ||
                    g.AwayConf.Equals(confAbbr, StringComparison.OrdinalIgnoreCase));
            }

            BowlWeekends.Clear();
            foreach (var wk in BuildBowlWeekends(bowlGames))
                BowlWeekends.Add(wk);
            OnPropertyChanged(nameof(HasBowlData));
        }

        // ── Helper: build bowl weekends from filtered games ───────────────

        private static List<BowlWeekendGroup> BuildBowlWeekends(IEnumerable<GameResult> bowlGames)
        {
            static DateTime WeekendSaturday(DateTime d)
            {
                int daysToSat = ((int)DayOfWeek.Saturday - (int)d.DayOfWeek + 7) % 7;
                return d.AddDays(daysToSat).Date;
            }

            var  weekendGroups = bowlGames
                .GroupBy(g =>
                {
                    var d = g.GameDate.ToDateTime();
                    return d.HasValue ? WeekendSaturday(d.Value) : DateTime.MaxValue;
                })
                .OrderBy(g => g.Key)
                .Select(wkGrp =>
                {
                    var label = wkGrp.Key == DateTime.MaxValue
                        ? "TBD"
                        : $"Weekend of {wkGrp.Key:ddd, MMM d}";
                    var days = wkGrp
                        .GroupBy(g => g.GroupHeader)
                        .OrderBy(g =>
                        {
                            try
                            {
                                var first = g.FirstOrDefault()?.GameDate?.ToDateTime();
                                return first ?? DateTime.MaxValue;
                            }
                            catch
                            {
                                return DateTime.MaxValue;
                            }
                        })
                        .Select(dayGrp => new BowlDayGroup(dayGrp.Key, dayGrp.ToList()))
                        .ToList();
                    return new BowlWeekendGroup(label, days);
                })
                .ToList();

            return weekendGroups;
        }

        // ── Conference filter (Championship view only) ────────────────────

        private void ApplyConferenceFilter()
        {
            Championships.Clear();

            // SelectedConference now stores Abbreviation directly — no DisplayToAbbr needed
            var conf     = _navState.SelectedConference;
            var confAbbr = conf == "All" ? null : conf;

            // ── Championships ─────────────────────────────────────────────
            var filteredChamps = confAbbr == null
                ? _allChampionships
                : _allChampionships.Where(c =>
                    c.Conference.Equals(confAbbr, StringComparison.OrdinalIgnoreCase))
                    .ToList();

            Championships.Clear();
            foreach (var c in filteredChamps)
                Championships.Add(c);

            // ── Bowls ─────────────────────────────────────────────────────
            var allGames = _cache.AllGames;
            if (allGames == null || allGames.Count == 0) return;

            var bowlGames = allGames.Where(g => g.SeasonType == "postseason");

            if (confAbbr != null)
            {
                bowlGames = bowlGames.Where(g =>
                    g.HomeConf.Equals(confAbbr, StringComparison.OrdinalIgnoreCase) ||
                    g.AwayConf.Equals(confAbbr, StringComparison.OrdinalIgnoreCase));
            }

            BowlWeekends.Clear();
            foreach (var wk in BuildBowlWeekends(bowlGames))
                BowlWeekends.Add(wk);
            OnPropertyChanged(nameof(HasBowlData));
        }

        // ── Event handlers ────────────────────────────────────────────────

        private async void OnNavStateChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != "FilterChanged") return;

            System.Diagnostics.Debug.WriteLine($"[Postseason] FilterChanged isMain={MainThread.IsMainThread} isActive={IsActive}");

            // Off-screen: defer. Mark stale so SyncPage reloads on next visit.
            if (!IsActive)
            {
                HasLoaded = false;
                return;
            }

            switch (_navState.LastFilterChange)
            {
                case FilterChangeReason.Year:
                    // Full reload — new year means new schedule + new championships
                    await LoadDataAsync();
                    break;

                case FilterChangeReason.Week:
                    // Week change only matters for championship qualifiers (not bowls/playoffs)
                    if (_selectedView != "Bowls" && _selectedView != "Playoffs")
                        await LoadDataAsync();
                    else
                        ApplyConferenceFilter();
                    break;

                case FilterChangeReason.Conference:
                    // Conference/favorites — refilter cached data only
                    ApplyConferenceFilter();
                    break;
            }
        }
        private void OnCacheUpdated()
        {
            // Only refilter if we've already loaded — avoids double render on initial load
            if (!HasLoaded) return;

            MainThread.BeginInvokeOnMainThread(RebuildPostseasonFromCache);
        }
    }
}

// ── Grouping models ──────────────────────────────────────────────────────────

namespace SaturdayPulse.ViewModels
{
    /// <summary>One round of the CFP bracket. Collapsible — tap the round header.</summary>
    public class PlayoffRound : INotifyPropertyChanged
    {
        public string RoundLabel { get; }
        public List<PlayoffDayGroup> Days { get; }

        private bool _isExpanded = true;
        public bool IsExpanded
        {
            get => _isExpanded;
            set { _isExpanded = value; OnPropertyChanged(); OnPropertyChanged(nameof(ExpandIcon)); }
        }

        public string ExpandIcon => _isExpanded ? "▼" : "▶";

        public PlayoffRound(string label, List<PlayoffDayGroup> days)
        {
            RoundLabel = label;
            Days       = days;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>One calendar day of CFP playoff games within a round (not collapsible).</summary>
    public class PlayoffDayGroup
    {
        public string DateLabel { get; }
        public List<SaturdayPulse.Models.GameResult> Games { get; }
        public PlayoffDayGroup(string dateLabel, List<SaturdayPulse.Models.GameResult> games)
        {
            DateLabel = dateLabel;
            Games     = games;
        }
    }

    /// <summary>One calendar day of bowl games within a weekend (not collapsible).</summary>
    public class BowlDayGroup
    {
        public string DateLabel { get; }
        public List<SaturdayPulse.Models.GameResult> Games { get; }
        public BowlDayGroup(string dateLabel, List<SaturdayPulse.Models.GameResult> games)
        {
            DateLabel = dateLabel;
            Games     = games;
        }
    }

    /// <summary>One weekend of bowl games. Collapsible — tap the weekend header.</summary>
    public class BowlWeekendGroup : INotifyPropertyChanged
    {
        public string WeekendLabel { get; }
        public List<BowlDayGroup> Days { get; }

        private bool _isExpanded = true;
        public bool IsExpanded
        {
            get => _isExpanded;
            set { _isExpanded = value; OnPropertyChanged(); OnPropertyChanged(nameof(ExpandIcon)); }
        }

        public string ExpandIcon => _isExpanded ? "▼" : "▶";

        public BowlWeekendGroup(string label, List<BowlDayGroup> days)
        {
            WeekendLabel = label;
            Days         = days;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
