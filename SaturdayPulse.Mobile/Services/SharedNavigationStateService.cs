using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using SaturdayPulse.ViewModels;

namespace SaturdayPulse.Services
{
    /// <summary>
    /// Single source of truth for year, week, conference selection,
    /// and user display preferences across all tabs.
    /// Registered as Singleton in MauiProgram.cs.
    /// </summary>
    public class SharedNavigationStateService : INotifyPropertyChanged
    {
        private int    _selectedYear       = 2025;
        private int    _selectedWeek       = 1;
        private string _selectedConference = "All";
        private bool   _showFavoritesFirst    = Preferences.Get("ShowFavoritesFirst", false);
        private string _defaultWeek           = Preferences.Get("DefaultWeek", "Current");
        private string _defaultConference     = Preferences.Get("DefaultConference", "All");
        private bool   _suppressNavReady   = false;

        // ── Year ──────────────────────────────────────────────────────────

        public int SelectedYear
        {
            get => _selectedYear;
            set
            {
                if (_selectedYear != value)
                {
                    _selectedYear = value;
                    _selectedWeek = 1;                          // reset to week 1 on year change
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(SelectedWeek));    // update week strip highlight
                    Weeks.Clear();
                    if (!_suppressNavReady) OnPropertyChanged("YearChanged");
                }
            }
        }

        // ── Week ──────────────────────────────────────────────────────────

        public int SelectedWeek
        {
            get => _selectedWeek;
            set
            {
                Console.WriteLine($"[NavState] SelectedWeek{_selectedWeek} set to {value}");

                if (_selectedWeek != value)
                {
                    _selectedWeek = value;
                    OnPropertyChanged();
                    SyncWeekItems();
             
                Console.WriteLine($"[NavState] SelectedWeek set to {value}, firing WeekChanged");

                    if (!_suppressNavReady) OnPropertyChanged("WeekChanged");
                }
            }
        }

        /// <summary>
        /// Set year and week atomically — fires a single YearChanged
        /// notification instead of two separate signals.
        /// </summary>
        public void SetYearAndWeek(int year, int week)
        {
            _suppressNavReady = true;
            try
            {
                SelectedYear = year;
                SelectedWeek = week;
            }
            finally
            {
                _suppressNavReady = false;
            }
            OnPropertyChanged("YearChanged");
        }

        // ── Conference ────────────────────────────────────────────────────

        public string SelectedConference
        {
            get => _selectedConference;
            set
            {
                if (_selectedConference != value)
                {
                    _selectedConference = value;
                    OnPropertyChanged();
                }
            }
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
                }
            }
        }

        // ── Default Week preference ───────────────────────────────────────

        /// <summary>
        /// "Week1" or "Current" — controls which week is selected on app launch.
        /// </summary>
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

        /// <summary>
        /// Conference abbreviation or "All" — applied to SelectedConference on launch.
        /// </summary>
        public string DefaultConference
        {
            get => _defaultConference;
            set
            {
                if (_defaultConference == value) return;
                _defaultConference = value;
                Preferences.Set("DefaultConference", value);
                OnPropertyChanged();
                // Apply immediately so the header updates without restarting
                SelectedConference = value;
            }
        }

        // ── Week items ────────────────────────────────────────────────────

        public ObservableCollection<WeekItem> Weeks { get; } = new();

        public void SetWeeks(IEnumerable<int> weekNumbers)
        {
            Weeks.Clear();
            foreach (var w in weekNumbers)
                Weeks.Add(new WeekItem { Week = w, IsSelected = w == _selectedWeek });
        }

        public void SyncWeekItems()
        {
            foreach (var w in Weeks)
                w.IsSelected = w.Week == _selectedWeek;
        }

        // ── Startup defaults ──────────────────────────────────────────────

        /// <summary>
        /// Called once after the schedule loads. Sets SelectedConference and
        /// SelectedWeek according to the user's saved DefaultConference /
        /// DefaultWeek preferences.
        ///
        /// "Current" = the highest week number whose earliest game date is
        /// on or before today.  Falls back to week 1 if all games are future.
        /// </summary>
        public void ApplyStartupDefaults<T>(
            IEnumerable<T> schedule,
            Func<T, int>       getWeek,
            Func<T, DateTime?> getDate)
        {
            // ── Conference ────────────────────────────────────────────────
            if (_defaultConference != "All")
                SelectedConference = _defaultConference;

            // ── Week ──────────────────────────────────────────────────────
            if (_defaultWeek == "Week1")
            {
                SelectedWeek = 1;
                return;
            }

            // "Current" — last week with at least one game date ≤ today.
            // If no games have been played yet (pre-season / off-season),
            // the season hasn't started so default to week 1.
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
                // Off-season or pre-season — no games played yet
                SelectedWeek = 1;
                return;
            }

            // At least one game has been played — find the latest such week
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

            SelectedWeek = currentWeek;
        }

        // ── INotifyPropertyChanged ────────────────────────────────────────

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
