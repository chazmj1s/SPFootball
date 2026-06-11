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
    /// Sole owner of year, week, and conference selection for the entire app.
    /// Consumer pages read these from SharedNavigationStateService and refilter
    /// on FilterChanged — they never build the week strip or resolve conferences.
    ///
    /// LoadYearContextAsync is the single place that warms the cache, builds the
    /// week strip, resolves the default week, and resolves the conference for a
    /// year. Both the startup path (InitializeAsync) and the user-driven year
    /// change (ApplyYearChangeAsync) route through it, then fire exactly one
    /// FilterChanged so consumers do a single refilter against warm state.
    ///
    /// Threading: ApplyYearChangeAsync / InitializeAsync are invoked on the main
    /// thread and never use ConfigureAwait(false), so the continuation that
    /// mutates nav state runs on the main thread. InitializeAsync MUST be called
    /// without Task.Run for this to hold (see MainPage).
    /// </summary>
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly SharedNavigationStateService _navState;
        private readonly GameDataApiService           _apiService;
        private readonly GameDataCacheService         _cache;
        private int  _selectedIndex = 0;
        private bool _yearChangeInFlight;   // re-entrancy guard (main-thread only)
        private bool _initialized;

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

            SelectWeekCommand = new Microsoft.Maui.Controls.Command<int>(week =>
            {
                AppLogger.Log($"[Week] Selected week={_navState.SelectedWeek} year={_navState.SelectedYear}");

                _navState.SelectedWeek = week;
            });

            // "All" is index 0 of the server list — no injection, no special-case.
            SelectConferenceCommand = new Microsoft.Maui.Controls.Command(async () =>
            {
                var available = _navState.AvailableConferences;
                if (available.Count == 0) return;

                var result = await Shell.Current.DisplayActionSheet(
                    "Conference", "Cancel", null,
                    available.Select(c => c.Name).ToArray());

                if (result is null or "Cancel") return;

                var picked = available.FirstOrDefault(c => c.Name == result);
                if (picked != null)
                    _navState.SelectedConference = picked.Abbreviation;
            });

            // Forward nav state changes to XAML bindings
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

        // ── Startup ───────────────────────────────────────────────────────

        /// <summary>
        /// Establishes the initial year context once at startup. Call this from
        /// MainPage WITHOUT Task.Run (so the nav-state continuation stays on the
        /// main thread). Fires FilterChanged(Year) so consumer pages render.
        /// </summary>
        public async Task InitializeAsync()
        {
            if (_initialized || _yearChangeInFlight) return;
            _yearChangeInFlight = true;
            IsLoading = true;

            try
            {
                var year = _navState.SelectedYear;
                AppLogger.Log($"[Init] start year={year}");

                await LoadYearContextAsync(year);

                // Year equals its default, so the setter won't fire — force it once.
                _navState.RaiseInitialFilterChanged();
                _initialized = true;
            }
            catch (Exception ex)
            {
                AppLogger.Log($"[Init] failed: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
                _yearChangeInFlight = false;
            }
        }

        // ── Year change orchestration ─────────────────────────────────────

        /// <summary>
        /// User explicitly picks a new year. Warms context, then fires one
        /// FilterChanged(Year). Re-taps are ignored while a change is loading.
        /// </summary>
        private async Task ApplyYearChangeAsync(int year)
        {
            if (_yearChangeInFlight) return;
            _yearChangeInFlight = true;
            IsLoading = true;

            try
            {
                AppLogger.Log($"[YearChange] start year={year}");

                await LoadYearContextAsync(year);

                // Single unified rebuild signal — consumers refilter from warm cache.
                _navState.SelectedYear = year;
            }
            catch (Exception ex)
            {
                AppLogger.Log($"[YearChange] failed year={year}: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
                _yearChangeInFlight = false;
            }
        }

        /// <summary>
        /// The single owner of year/week/conference setup. Warms the games cache,
        /// builds the week strip, resolves the default week, and resolves the
        /// conference — all before any FilterChanged fires. Does NOT fire it.
        ///
        /// Conferences + games load concurrently. forceReload is gated to the
        /// in-progress season; historical years (immutable) serve from warm cache,
        /// which also keeps GameDataCacheService from firing CacheUpdated and
        /// causing consumers to double-render.
        /// </summary>
        private async Task LoadYearContextAsync(int year)
        {
            bool currentSeason = year >= DateTime.Now.Year;

            // Fetch + deserialize + conference/tier stamping all happen inside these
            // calls and are CPU-heavy on device, so run the whole thing on a
            // background thread. The continuation after the await resumes on the main
            // thread (this method is invoked on the main thread with no
            // ConfigureAwait(false)), so every nav-state mutation below stays UI-safe.
            var (conferences, games) = await Task.Run(async () =>
            {
                var confTask  = _apiService.GetConferencesForYearAsync(year);
                var gamesTask = _cache.GetGamesForYearAsync(year, forceReload: currentSeason);
                await Task.WhenAll(confTask, gamesTask);
                return (await confTask, await gamesTask);
            });

            // ── Main-thread continuation: nav-state mutation is UI-safe here ──

            if (conferences != null)
                _navState.SetAvailableConferences(conferences);

            var weeks = games.Select(g => g.Week).Distinct().OrderBy(w => w).ToList();
            _navState.SetWeeks(weeks);

            _navState.ApplyStartupDefaults(
                games,
                g => g.Week,
                g =>
                {
                    if (string.IsNullOrWhiteSpace(g.GameDate)) return null;
                    var dateStr = $"{g.GameDate} {year}";
                    return DateTime.TryParse(dateStr, out var d) ? d : (DateTime?)null;
                });

            // Conference resolution (MainView owns this): keep the current pick if
            // it still exists this year, else fall to the default, else "All".
            var resolved =
                IsConferenceValid(_navState.SelectedConference) ? _navState.SelectedConference
              : IsConferenceValid(_navState.DefaultConference)  ? _navState.DefaultConference
              : "All";

            _navState.SetConferenceSilent(resolved);
        }

        private bool IsConferenceValid(string abbreviation) =>
            string.Equals(abbreviation, "All", StringComparison.OrdinalIgnoreCase) ||
            _navState.AvailableConferences.Any(c =>
                string.Equals(c.Abbreviation, abbreviation, StringComparison.OrdinalIgnoreCase));

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
