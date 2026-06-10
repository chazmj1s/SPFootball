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
    /// Nav state changes fire on the main thread as always.
    /// HTTP calls inside LoadDataAsync methods are wrapped in Task.Run.
    /// No startup initialization here — MainPage handles that directly
    /// by calling ScheduleViewModel.LoadDataAsync via Task.Run.
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
                _navState.SelectedWeek = week;
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
                    _navState.SelectedConference = "All";
                    return;
                }

                var picked = available.FirstOrDefault(c => c.Name == result);
                _navState.SelectedConference = picked?.Abbreviation ?? result;
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

        // ── Year change orchestration ─────────────────────────────────────

        /// <summary>
        /// Called when user explicitly picks a new year.
        /// HTTP calls wrapped in Task.Run to keep main thread free.
        /// Not called on startup — MainPage handles that directly.
        /// </summary>
        private async Task ApplyYearChangeAsync(int year)
        {
            // Cache reload on background thread
            var games = await Task.Run(async () =>
                await _cache.GetGamesForYearAsync(year, forceReload: true));

            // Conference dropdown refresh on background thread
            await Task.Run(async () => await RefreshConferencesAsync(year));

            // Week list — SetWeeks marshals to main thread internally
            System.Diagnostics.Debug.WriteLine($"[YearChange] Before SetWeeks isMain={MainThread.IsMainThread}");
            var weeks = games.Select(g => g.Week).Distinct().OrderBy(w => w).ToList();
            _navState.SetWeeks(weeks);
            System.Diagnostics.Debug.WriteLine($"[YearChange] After SetWeeks isMain={MainThread.IsMainThread}");
            _navState.ApplyStartupDefaults(
                games,
                g => g.Week,
                g =>
                {
                    if (string.IsNullOrWhiteSpace(g.GameDate)) return null;
                    var dateStr = $"{g.GameDate} {year}";
                    return DateTime.TryParse(dateStr, out var d) ? d : (DateTime?)null;
                });

            // Validate conference still exists in new year
            var validDefault = _navState.AvailableConferences.Any(c =>
                string.Equals(c.Abbreviation, _navState.DefaultConference,
                    StringComparison.OrdinalIgnoreCase))
                ? _navState.DefaultConference : "All";

            _navState.SetConferenceSilent(validDefault);

            // Fire FilterChanged(Year) — all ViewModels rebuild from hot cache
            _navState.SelectedYear = year;
        }

        private async Task RefreshConferencesAsync(int year)
        {
            var conferences = await _apiService.GetConferencesForYearAsync(year);
            if (conferences != null)
                _navState.SetAvailableConferencesAsync(conferences);
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
