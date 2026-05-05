using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using NCAA_Power_Ratings.Mobile.Helpers;
using NCAA_Power_Ratings.Mobile.Services;

namespace NCAA_Power_Ratings.Mobile.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly SharedNavigationStateService _navState;
        private int _selectedIndex = 0;

        public MainViewModel(SharedNavigationStateService navState)
        {
            _navState = navState;

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
                var years = Enumerable.Range(1965, 2025 - 1965 + 1)
                    .Select(y => y.ToString())
                    .Reverse()
                    .ToArray();

                var result = await Shell.Current.DisplayActionSheet(
                    "Select Year", "Cancel", null, years);

                if (result != null && result != "Cancel" && int.TryParse(result, out int year))
                    _navState.SelectedYear = year;
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
                var options = new List<string> { "All" };
                options.AddRange(ConferenceHelper.OrderedConferences.Select(c => c.Display));

                var result = await Shell.Current.DisplayActionSheet(
                    "Conference", "Cancel", null, options.ToArray());

                if (result != null && result != "Cancel")
                    _navState.SelectedConference = result;
            });

            // Forward nav state changes to XAML bindings
            _navState.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(SharedNavigationStateService.SelectedYear))
                    OnPropertyChanged(nameof(SelectedYear));
                if (e.PropertyName == nameof(SharedNavigationStateService.SelectedWeek))
                    OnPropertyChanged(nameof(SelectedWeek));
                if (e.PropertyName == nameof(SharedNavigationStateService.SelectedConference))
                    OnPropertyChanged(nameof(SelectedConference));
            };
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

        public int    SelectedYear        => _navState.SelectedYear;
        public int    SelectedWeek        => _navState.SelectedWeek;
        public string SelectedConference  => _navState.SelectedConference;
        public ObservableCollection<WeekItem> Weeks => _navState.Weeks;

        public ICommand SelectYearCommand       { get; }
        public ICommand SelectWeekCommand       { get; }
        public ICommand PreviousWeekCommand     { get; }
        public ICommand NextWeekCommand         { get; }
        public ICommand SelectConferenceCommand { get; }

        public SharedNavigationStateService NavState => _navState;

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
