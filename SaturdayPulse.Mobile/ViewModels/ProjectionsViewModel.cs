using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using SaturdayPulse.Helpers;
using SaturdayPulse.Models;
using SaturdayPulse.Services;

namespace SaturdayPulse.ViewModels
{
    public class ProjectionsViewModel : BaseViewModel
    {
        private readonly GameDataApiService           _apiService;
        private readonly SharedNavigationStateService _navState;

        private List<ChampionshipMatchup> _allChampionships = new();
        private bool   _isBusy;
        private string _selectedView  = "Championship";
        private string _statusMessage = string.Empty;

        public ProjectionsViewModel(
            GameDataApiService apiService,
            FollowService followService,
            SharedNavigationStateService navState)
            : base(followService)
        {
            _apiService = apiService;
            _navState   = navState;

            LoadDataCommand = new Microsoft.Maui.Controls.Command(async () => await LoadDataAsync());

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

            _navState.PropertyChanged += OnNavStateChanged;

        }

        // ── Bindable collections ──────────────────────────────────────────

        public ObservableCollection<ChampionshipMatchup> Championships { get; } = new();

        // ── Bindable properties ───────────────────────────────────────────

        public bool IsBusy
        {
            get => _isBusy;
            set { _isBusy = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsLoading)); }
        }

        public bool IsLoading => _isBusy;
        public bool HasLoaded { get; set; }

        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
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
                OnPropertyChanged(nameof(IsSandboxView));
            }
        }

        public bool IsChampionshipView => _selectedView == "Championship";
        public bool IsPlayoffsView     => _selectedView == "Playoffs";
        public bool IsBowlsView        => _selectedView == "Bowls";
        public bool IsSandboxView      => _selectedView == "Sandbox";

        // ── Commands ──────────────────────────────────────────────────────

        public ICommand LoadDataCommand               { get; }
        public ICommand SelectViewCommand             { get; }
        public ICommand ToggleMatchupExpandCommand    { get; }
        public ICommand ToggleContendersExpandCommand { get; }

        // ── Load ──────────────────────────────────────────────────────────

        public async Task LoadDataAsync()
        {
            if (IsBusy) return;
            IsBusy = true;
            StatusMessage = "Loading projections...";

            try
            {
                var championships = await _apiService.GetProjectedChampionshipQualifiersAsync(
                    _navState.SelectedYear, _navState.SelectedWeek);

                if (championships == null)
                {
                    StatusMessage = "Failed to load projections";
                    return;
                }

                _allChampionships = championships;
                ApplyConferenceFilter();

                StatusMessage = $"Week {_navState.SelectedWeek} projections";
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

        // ── Conference filter ─────────────────────────────────────────────

        private void ApplyConferenceFilter()
        {
            var conf = _navState.SelectedConference;

            var filteredChamps = conf == "All"
                ? _allChampionships
                : _allChampionships.Where(c =>
                {
                    var abbr = ConferenceHelper.DisplayToAbbr(conf);
                    return c.Conference.Equals(abbr, StringComparison.OrdinalIgnoreCase);
                }).ToList();

            Championships.Clear();
            foreach (var c in filteredChamps)
                Championships.Add(c);
        }

        private async void OnNavStateChanged(object sender, PropertyChangedEventArgs e)
        {
            // User week tap — re-filter client-side only, no server call
            if (e.PropertyName == nameof(SharedNavigationStateService.SelectedWeek))
                await LoadDataAsync();

            if (e.PropertyName == nameof(SharedNavigationStateService.SelectedConference))
                ApplyConferenceFilter();
        }

    }
}
