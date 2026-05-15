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
        private bool   _showFavoritesFirst = Preferences.Get("ShowFavoritesFirst", false);

        // ── Year ──────────────────────────────────────────────────────────

        public int SelectedYear
        {
            get => _selectedYear;
            set
            {
                if (_selectedYear != value)
                {
                    _selectedYear = value;
                    OnPropertyChanged();
                    Weeks.Clear();
                }
            }
        }

        // ── Week ──────────────────────────────────────────────────────────

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
                }
            }
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

        public void SetDefaultWeek(IEnumerable<int> playedWeeks)
        {
            var weeks = playedWeeks.ToList();
            if (!weeks.Any()) return;
            _selectedWeek = weeks.Max();
            OnPropertyChanged(nameof(SelectedWeek));
            SyncWeekItems();
        }

        // ── INotifyPropertyChanged ────────────────────────────────────────

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
