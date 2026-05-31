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
        public ObservableCollection<PlayoffRound> PlayoffRounds { get; } = new();
        public ObservableCollection<BowlDayGroup> BowlDays { get; } = new();
        private bool _playoffLoaded;
        private bool _bowlsLoaded;

        // ── Bindable properties ───────────────────────────────────────────

        public bool IsBusy
        {
            get => _isBusy;
            set { _isBusy = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsLoading)); }
        }

        public bool IsLoading => _isBusy;
        public bool HasLoaded       { get; set; }
        public bool HasPlayoffData  => PlayoffRounds.Any();
        public bool HasBowlData     => BowlDays.Any();

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
                // ── Championship projections ──────────────────────────────
                var championships = await _apiService.GetProjectedChampionshipQualifiersAsync(
                    _navState.SelectedYear, _navState.SelectedWeek);

                if (championships != null)
                {
                    _allChampionships = championships;
                    ApplyConferenceFilter();
                }

                // ── Postseason games (playoff + bowl) — load once per year ──
                if (!_playoffLoaded && !_bowlsLoaded)
                {
                    var postseason = await _apiService.GetPostseasonAsync(_navState.SelectedYear);
                    if (postseason != null && postseason.Count > 0)
                    {
                        var weekToRound = new Dictionary<int, string>
                        {
                            { 17, "First Round" },
                            { 19, "Quarterfinals" },
                            { 20, "Semifinals" },
                            { 21, "National Championship" }
                        };

                        var playoffRounds = postseason
                            .Where(g => g.SeasonType == "playoff")
                            .GroupBy(g => g.Week)
                            .OrderBy(g => g.Key)
                            .Select(weekGrp =>
                            {
                                var label = weekToRound.TryGetValue(weekGrp.Key, out var r) ? r : $"Week {weekGrp.Key}";
                                var days  = weekGrp
                                    .GroupBy(g => g.GroupHeader)
                                    .OrderBy(g => g.Key)
                                    .Select(dayGrp => new PlayoffDayGroup(dayGrp.Key, dayGrp.ToList()))
                                    .ToList();
                                return new PlayoffRound(label, days);
                            });

                        PlayoffRounds.Clear();
                        foreach (var round in playoffRounds)
                            PlayoffRounds.Add(round);
                        _playoffLoaded = true;
                        OnPropertyChanged(nameof(HasPlayoffData));

                        var bowlGroups = postseason
                            .Where(g => g.SeasonType == "postseason")
                            .GroupBy(g => g.GameDate ?? "TBD")
                            .OrderBy(g => g.Key);

                        BowlDays.Clear();
                        foreach (var grp in bowlGroups)
                            BowlDays.Add(new BowlDayGroup(grp.Key, grp.OrderBy(g => g.GameDate).ToList()));
                        _bowlsLoaded = true;
                        OnPropertyChanged(nameof(HasBowlData));
                    }
                }

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
            if (e.PropertyName == nameof(SharedNavigationStateService.SelectedYear))
            {
                _playoffLoaded = false;
                _bowlsLoaded   = false;
                PlayoffRounds.Clear();
                BowlDays.Clear();
            }

            // Week and conference changes only affect Championship view
            if (_selectedView == "Playoffs" || _selectedView == "Bowls") return;

            if (e.PropertyName == nameof(SharedNavigationStateService.SelectedWeek))
                await LoadDataAsync();

            if (e.PropertyName == nameof(SharedNavigationStateService.SelectedConference))
                ApplyConferenceFilter();
        }

    }
}

// ── Grouping models ───────────────────────────────────────────────────────────

namespace SaturdayPulse.ViewModels
{
    /// <summary>One round of the CFP bracket (First Round / Quarterfinals / Semifinals / Championship).</summary>
    public class PlayoffRound
    {
        public string RoundLabel { get; }
        public List<PlayoffDayGroup> Days { get; }
        public PlayoffRound(string label, List<PlayoffDayGroup> days)
        {
            RoundLabel = label;
            Days       = days;
        }
    }

    /// <summary>One calendar day of CFP playoff games within a round.</summary>
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

    /// <summary>One calendar day of bowl games.</summary>
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
}
