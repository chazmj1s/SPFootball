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
        private readonly GameDataApiService    _apiService;
        private readonly SharedNavigationStateService _navState;

        private List<ProjectedTeamStanding>  _allStandings     = new();
        private List<ChampionshipMatchup>    _allChampionships = new();
        private bool   _isBusy;
        private string _selectedView  = "Standings";
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

            ToggleTeamExpandCommand = new Microsoft.Maui.Controls.Command<ProjectedTeamStanding>(team =>
            {
                if (team != null) team.IsExpanded = !team.IsExpanded;
            });

            ToggleChartExpandCommand = new Microsoft.Maui.Controls.Command<ProjectedTeamStanding>(team =>
            {
                if (team != null) team.IsChartExpanded = !team.IsChartExpanded;
            });

            ToggleMatchupExpandCommand = new Microsoft.Maui.Controls.Command<ChampionshipMatchup>(matchup =>
            {
                if (matchup != null) matchup.IsExpanded = !matchup.IsExpanded;
            });

            ToggleContendersExpandCommand = new Microsoft.Maui.Controls.Command<ChampionshipMatchup>(matchup =>
            {
                if (matchup != null) matchup.IsContendersExpanded = !matchup.IsContendersExpanded;
            });

            // React to shared navigation changes
            _navState.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(SharedNavigationStateService.SelectedYear) ||
                    e.PropertyName == nameof(SharedNavigationStateService.SelectedWeek))
                    _ = LoadDataAsync();
                if (e.PropertyName == nameof(SharedNavigationStateService.SelectedConference))
                    ApplyConferenceFilter();
            };
        }

        // ── Bindable collections ──────────────────────────────────────────

        public ObservableCollection<ProjectedTeamStanding> Standings    { get; } = new();
        public ObservableCollection<ChampionshipMatchup>  Championships { get; } = new();

        // ── Bindable properties ───────────────────────────────────────────

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

        public string SelectedView
        {
            get => _selectedView;
            set
            {
                _selectedView = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsStandingsView));
                OnPropertyChanged(nameof(IsChampionshipView));
            }
        }

        public bool IsStandingsView    => _selectedView == "Standings";
        public bool IsChampionshipView => _selectedView == "Championship";

        // ── Commands ──────────────────────────────────────────────────────

        public ICommand LoadDataCommand               { get; }
        public ICommand SelectViewCommand             { get; }
        public ICommand ToggleTeamExpandCommand       { get; }
        public ICommand ToggleChartExpandCommand      { get; }
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
                var standingsTask     = _apiService.GetProjectedStandingsAsync(
                    _navState.SelectedYear, _navState.SelectedWeek);
                var championshipsTask = _apiService.GetProjectedChampionshipQualifiersAsync(
                    _navState.SelectedYear, _navState.SelectedWeek);

                await Task.WhenAll(standingsTask, championshipsTask);

                var standings     = standingsTask.Result;
                var championships = championshipsTask.Result;

                if (standings == null || championships == null)
                {
                    StatusMessage = "Failed to load projections";
                    return;
                }

                // Compute projected finish rank within each conference
                var byConference = standings
                    .GroupBy(s => s.Conference)
                    .ToDictionary(g => g.Key, g => g
                        .OrderByDescending(s => s.ProjectedWinPct)
                        .ThenByDescending(s => s.ProjectedWins)
                        .ToList());

                foreach (var conf in byConference.Values)
                    for (int i = 0; i < conf.Count; i++)
                        conf[i].ProjectedFinish = i + 1;

                _allStandings     = standings;
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

            // Filter standings
            var filteredStandings = conf == "All"
                ? _allStandings
                : _allStandings.Where(s =>
                {
                    var abbr = ConferenceHelper.DisplayToAbbr(conf);
                    return s.Conference.Equals(abbr, StringComparison.OrdinalIgnoreCase);
                }).ToList();

            Standings.Clear();
            foreach (var s in filteredStandings)
                Standings.Add(s);

            // Filter championships
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
    }
}
