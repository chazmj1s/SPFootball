using SaturdayPulse.Models;
using SaturdayPulse.ViewModels;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SaturdayPulse.Services
{
    /// <summary>
    /// Single source of truth for year, week, conference selection,
    /// and user display preferences across all tabs.
    /// Registered as Singleton in MauiProgram.cs.
    ///
    /// FilterChanged is the unified signal for all nav-state changes.
    /// LastFilterChange tells subscribers what triggered it so they can
    /// decide whether to hit the server or just refilter cached data.
    /// </summary>
    public class SharedNavigationStateService : INotifyPropertyChanged
    {
        private int    _selectedYear       = 2025;
        private int    _selectedWeek       = 1;
        private string _selectedConference = "All";
        private bool   _showFavoritesFirst = Preferences.Get("ShowFavoritesFirst", false);
        private string _defaultWeek        = Preferences.Get("DefaultWeek", "Current");
        private string _defaultConference  = Preferences.Get("DefaultConference", "All");
        private ObservableCollection<ConferenceInfo> _availableConferences = new();

        // ── Filter change reason ──────────────────────────────────────────

        /// <summary>
        /// What triggered the most recent FilterChanged signal.
        /// Read by ViewModels in their FilterChanged handler to decide
        /// whether to reload from the server or just refilter cached data.
        /// </summary>
        public FilterChangeReason LastFilterChange { get; private set; } = FilterChangeReason.Year;

        private void FireFilterChanged(FilterChangeReason reason)
        {
            var caller = new System.Diagnostics.StackFrame(2, false).GetMethod()?.Name ?? "unknown";
            System.Diagnostics.Debug.WriteLine($"[FilterChanged] reason={reason} caller={caller} isMain={MainThread.IsMainThread}");

            LastFilterChange = reason;
            OnPropertyChanged("FilterChanged");
        }

        // ── Year ──────────────────────────────────────────────────────────

        /// <summary>
        /// Set by MainViewModel after pre-warming the cache.
        /// Fires FilterChanged(Year) — do not set directly from other ViewModels.
        /// </summary>
        public int SelectedYear
        {
            get => _selectedYear;
            set
            {
                if (_selectedYear != value)
                {
                    _selectedYear = value;
                    OnPropertyChanged();
                    FireFilterChanged(FilterChangeReason.Year);
                }
            }
        }

        // ── Week ──────────────────────────────────────────────────────────

        /// <summary>
        /// Set by MainViewModel. Fires FilterChanged(Week).
        /// Do not set directly from other ViewModels.
        /// </summary>
        public int SelectedWeek
        {
            get => _selectedWeek;
            set
            {
                if (_selectedWeek != value)
                {
                    _selectedWeek = value;
                    OnPropertyChanged();
                    SyncWeekItems();
                    FireFilterChanged(FilterChangeReason.Week);
                }
            }
        }

        // ── Conference ────────────────────────────────────────────────────

        /// <summary>
        /// Stores the conference Abbreviation (e.g. "SEC", "SWC", "All").
        /// Set by MainViewModel. Fires FilterChanged(Conference).
        /// </summary>
        public string SelectedConference
        {
            get => _selectedConference;
            set
            {
                if (_selectedConference != value)
                {
                    _selectedConference = value;
                    OnPropertyChanged();
                    FireFilterChanged(FilterChangeReason.Conference);
                }
            }
        }

        // ── Available conferences ─────────────────────────────────────────

        public ObservableCollection<ConferenceInfo> AvailableConferences => _availableConferences;

        /// <summary>
        /// Replaces the available conference list. If the currently selected
        /// abbreviation no longer exists in the new year, resets to "All"
        /// (without firing FilterChanged — year change handles that).
        /// </summary>
        public void SetAvailableConferencesAsync(IEnumerable<ConferenceInfo> conferences)
        {
            var list = conferences.ToList(); // materialize before crossing threads
            MainThread.InvokeOnMainThreadAsync(() =>
            {
                _availableConferences.Clear();
                foreach (var c in list)
                    _availableConferences.Add(c);

                if (SelectedConference != "All")
                {
                    var stillValid = _availableConferences.Any(c =>
                        string.Equals(c.Abbreviation, SelectedConference, StringComparison.OrdinalIgnoreCase));
                    if (!stillValid)
                    {
                        _selectedConference = "All";
                        OnPropertyChanged(nameof(SelectedConference));
                    }
                }
            });
        }

        /// <summary>
        /// Sets SelectedConference without firing FilterChanged.
        /// Used by MainViewModel during year-change orchestration so that
        /// conference reset and year change fire a single FilterChanged(Year).
        /// </summary>
        public void SetConferenceSilent(string abbreviation)
        {
            if (_selectedConference == abbreviation) return;
            _selectedConference = abbreviation;
            OnPropertyChanged(nameof(SelectedConference));
        }

        // ── Show Favorites First ──────────────────────────────────────────

        public bool ShowFavoritesFirst
        {
            get => _showFavoritesFirst;
            set
            {
                if (_showFavoritesFirst != value)
                {
                    _showFavoritesFirst = value;
                    Preferences.Set("ShowFavoritesFirst", value);
                    OnPropertyChanged();
                    FireFilterChanged(FilterChangeReason.Conference); // local refilter only
                }
            }
        }

        // ── Default Week preference ───────────────────────────────────────

        public string DefaultWeek
        {
            get => _defaultWeek;
            set
            {
                if (_defaultWeek == value) return;
                _defaultWeek = value;
                Preferences.Set("DefaultWeek", value);
                OnPropertyChanged();
            }
        }

        // ── Default Conference preference ─────────────────────────────────

        public string DefaultConference
        {
            get => _defaultConference;
            set
            {
                if (_defaultConference == value) return;
                _defaultConference = value;
                Preferences.Set("DefaultConference", value);
                OnPropertyChanged();
                SelectedConference = value;  // fires FilterChanged(Conference)
            }
        }

        // ── Week items ────────────────────────────────────────────────────

        public ObservableCollection<WeekItem> Weeks { get; } = new();

        public void SetWeeks(IEnumerable<int> weekNumbers)
        {
            var weekList = weekNumbers.ToList(); // materialize before crossing threads
            MainThread.BeginInvokeOnMainThread(() =>
            {
                Weeks.Clear();
                foreach (var w in weekList)
                    Weeks.Add(new WeekItem { Week = w, IsSelected = w == _selectedWeek });
            });
        }

        public void SyncWeekItems()
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                foreach (var w in Weeks)
                    w.IsSelected = w.Week == _selectedWeek;
            });
        }

        // ── Startup defaults ──────────────────────────────────────────────

        /// <summary>
        /// Called by MainViewModel after the schedule loads for a new year.
        /// Sets SelectedWeek according to DefaultWeek preference without
        /// firing FilterChanged — MainViewModel fires it once after all
        /// state is settled.
        /// </summary>
        public void ApplyStartupDefaults<T>(
            IEnumerable<T> schedule,
            Func<T, int>       getWeek,
            Func<T, DateTime?> getDate)
        {
            if (_defaultWeek == "Week1")
            {
                _selectedWeek = 1;
                OnPropertyChanged(nameof(SelectedWeek));
                SyncWeekItems();
                return;
            }

            var today = DateTime.Today;
            var games = schedule.ToList();

            var seasonStartDate = games
                .Select(g => getDate(g))
                .Where(d => d.HasValue)
                .Select(d => d!.Value.Date)
                .DefaultIfEmpty(DateTime.MaxValue)
                .Min();

            if (today < seasonStartDate)
            {
                _selectedWeek = 1;
                OnPropertyChanged(nameof(SelectedWeek));
                SyncWeekItems();
                return;
            }

            var currentWeek = games
                .GroupBy(g => getWeek(g))
                .Where(grp => grp.Any(g =>
                {
                    var d = getDate(g);
                    return d.HasValue && d.Value.Date <= today;
                }))
                .Select(grp => grp.Key)
                .DefaultIfEmpty(1)
                .Max();

            _selectedWeek = currentWeek;
            OnPropertyChanged(nameof(SelectedWeek));
            SyncWeekItems();
        }

        // ── INotifyPropertyChanged ────────────────────────────────────────

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // ── Filter change reason ──────────────────────────────────────────────

    public enum FilterChangeReason
    {
        Year,           // year changed — ViewModels may need server call
        Week,           // week changed — Rankings needs server call, others refilter
        Conference      // conference/favorites changed — all ViewModels refilter only
    }
}
