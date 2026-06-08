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
    /// Owns year, week, and conference selection for the entire app.
    /// On year change: pre-warms cache + conferences in parallel, extracts
    /// weeks, applies startup defaults, then fires FilterChanged(Year) by
    /// setting SelectedYear — all ViewModels rebuild from a hot cache.
    /// </summary>
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly SharedNavigationStateService _navState;
        private readonly GameDataApiService           _apiService;
        private readonly GameDataCacheService         _cache;
        private int _selectedIndex = 0;

        public MainViewModel(
            SharedNavigationStateService navState,
            GameDataApiService apiService,
            GameDataCacheService cache)
        {
            _navState   = navState;
            _apiService = apiService;
            _cache      = cache;

            SelectTabCommand = new Microsoft.Maui.Controls.Command<int>(idx =>
            {
                SelectedIndex = idx;
            });

            NextTabCommand = new Microsoft.Maui.Controls.Command(() =>
            {
                if (SelectedIndex < TabItems.Count - 1) SelectedIndex++;
            });

            PreviousTabCommand = new Microsoft.Maui.Controls.Command(() =>
            {
                if (SelectedIndex > 0) SelectedIndex--;
            });

            // ── Year change — MainViewModel orchestrates the full flow ────
            SelectYearCommand = new Microsoft.Maui.Controls.Command(async () =>
            {
                var years = Enumerable.Range(1965, DateTime.Now.Year - 1965 + 1)
                    .Select(y => y.ToString())
                    .Reverse()
                    .ToArray();

                var result = await Shell.Current.DisplayActionSheet(
                    "Select Year", "Cancel", null, years);

                if (result == null || result == "Cancel" || !int.TryParse(result, out int year))
                    return;

                await ApplyYearChangeAsync(year);
            });

            // ── Week — owned here, delegated to navState ─────────────────
            SelectWeekCommand = new Microsoft.Maui.Controls.Command<int>(week =>
            {
                _navState.SelectedWeek = week;  // fires FilterChanged(Week)
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

            // ── Conference — owned here, delegated to navState ────────────
            SelectConferenceCommand = new Microsoft.Maui.Controls.Command(async () =>
            {
                var available = _navState.AvailableConferences.Any()
                    ? _navState.AvailableConferences.ToList()
                    : ConferenceHelper.OrderedConferences
                        .Select(c => new ConferenceInfo { Name = c.Display, Abbreviation = c.Abbr, Tier = "" })
                        .ToList();

                var options = new List<string> { "All" };
                options.AddRange(available.Select(c => c.Name));

                var result = await Shell.Current.DisplayActionSheet(
                    "Conference", "Cancel", null, options.ToArray());

                if (result == null || result == "Cancel") return;

                if (result == "All")
                {
                    _navState.SelectedConference = "All";   // fires FilterChanged(Conference)
                    return;
                }

                var picked = available.FirstOrDefault(c => c.Name == result);
                _navState.SelectedConference = picked?.Abbreviation ?? result;
            });

            // Forward nav state property changes to XAML bindings
            _navState.PropertyChanged += (s, e) =>
            {
                switch (e.PropertyName)
                {
                    case nameof(SharedNavigationStateService.SelectedYear):
                        OnPropertyChanged(nameof(SelectedYear));
                        break;
                    case nameof(SharedNavigationStateService.SelectedWeek):
                        OnPropertyChanged(nameof(SelectedWeek));
                        break;
                    case nameof(SharedNavigationStateService.SelectedConference):
                        OnPropertyChanged(nameof(SelectedConference));
                        break;
                    case nameof(SharedNavigationStateService.ShowFavoritesFirst):
                        OnPropertyChanged(nameof(ShowFavoritesFirst));
                        break;
                }
            };

        }

        // ── Startup initialization ────────────────────────────────────────

        public bool HasInitialized { get; private set; }

        /// <summary>
        /// Called once by MainPage after UI is ready.
        /// Pre-warms cache + conferences, builds week list, fires FilterChanged.
        /// Kept out of the constructor to avoid blocking the main thread during launch.
        /// </summary>
        public async Task InitializeAsync()
        {
            if (HasInitialized) return;
            HasInitialized = true;
            await ApplyYearChangeAsync(_navState.SelectedYear, isStartup: true);
        }

        // ── Year change orchestration ─────────────────────────────────────

        private async Task ApplyYearChangeAsync(int year, bool isStartup = false)
        {
            System.Diagnostics.Debug.WriteLine($"[API] Apply Year Change Year: {year} : Startup: {isStartup}");

            // 1. Pre-warm cache + refresh conference dropdown in parallel
            var games = await _cache.GetGamesForYearAsync(year, forceReload: !isStartup);
            await RefreshConferencesAsync(year);

            // 2. Build week list from the loaded schedule
            var weeks = games.Select(g => g.Week).Distinct().OrderBy(w => w).ToList();
            _navState.SetWeeks(weeks);

            System.Diagnostics.Debug.WriteLine($"[API] Set Weeks on NavState to: {_navState.SelectedWeek}");

            // 3. Apply week + conference defaults (sets backing fields silently —
            //    no FilterChanged fires here; we fire once below via SelectedYear)
            _navState.ApplyStartupDefaults(
                games,
                g => g.Week,
                g =>
                {
                    if (string.IsNullOrWhiteSpace(g.GameDate)) return null;
                    var dateStr = $"{g.GameDate} {year}";
                    return DateTime.TryParse(dateStr, out var d) ? d : (DateTime?)null;
                });

            // 4. On year change (not startup), reset conference to saved default
            if (!isStartup)
            {
                var defaultConf = _navState.DefaultConference;
                // Set backing field directly — FilterChanged fires once below via SelectedYear
                if (_navState.SelectedConference != defaultConf)
                {
                    // Use SetAvailableConferences to validate the default still exists
                    // for this year; it resets to All if not
                    var validDefault = _navState.AvailableConferences.Any(c =>
                        string.Equals(c.Abbreviation, defaultConf, StringComparison.OrdinalIgnoreCase))
                        ? defaultConf : "All";

                    // Write to backing field via property to get the OnPropertyChanged
                    // but suppress the FilterChanged — SelectedYear fires it below
                    _navState.SetConferenceSilent(validDefault);
                }
            }

            System.Diagnostics.Debug.WriteLine($"[API] Set Conference on navState to: {_navState.SelectedConference}");


            // 5. Fire FilterChanged(Year) — cache is hot, weeks/conference are set,
            //    all ViewModels rebuild from correct state in one signal
            if (!isStartup)
                _navState.SelectedYear = year;  // fires FilterChanged(Year)
        }

        private async Task RefreshConferencesAsync(int year)
        {
            var conferences = await _apiService.GetConferencesForYearAsync(year);
            if (conferences != null)
                _navState.SetAvailableConferences(conferences);
        }

        // ── Tab nav ───────────────────────────────────────────────────────

        public ObservableCollection<TabItem> TabItems { get; } = new();
        public ObservableCollection<object>  Pages    { get; } = new();

        public int SelectedIndex
        {
            get => _selectedIndex;
            set
            {
                if (_selectedIndex != value)
                {
                    _selectedIndex = value;
                    OnPropertyChanged();
                }
            }
        }

        public ICommand SelectTabCommand   { get; }
        public ICommand NextTabCommand     { get; }
        public ICommand PreviousTabCommand { get; }

        // ── Shared navigation proxy properties ────────────────────────────

        public int    SelectedYear       => _navState.SelectedYear;
        public int    SelectedWeek       => _navState.SelectedWeek;
        public string SelectedConference => _navState.SelectedConference;
        public bool   ShowFavoritesFirst => _navState.ShowFavoritesFirst;

        public ObservableCollection<WeekItem> Weeks => _navState.Weeks;

        public ICommand SelectYearCommand       { get; }
        public ICommand SelectWeekCommand       { get; }
        public ICommand PreviousWeekCommand     { get; }
        public ICommand NextWeekCommand         { get; }
        public ICommand SelectConferenceCommand { get; }

        public SharedNavigationStateService NavState => _navState;

        // ── Loading state ─────────────────────────────────────────────────

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                if (_isLoading != value)
                {
                    _isLoading = value;
                    OnPropertyChanged();
                }
            }
        }

        // ── INotifyPropertyChanged ────────────────────────────────────────

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // ── Tab item ──────────────────────────────────────────────────────────

    public class TabItem : INotifyPropertyChanged
    {
        private bool _isSelected;

        public string Label { get; init; } = string.Empty;
        public int    Index { get; init; }

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
