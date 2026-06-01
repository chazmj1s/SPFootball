using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SaturdayPulse.Models;
using SaturdayPulse.Services;

namespace SaturdayPulse.ViewModels
{
    /// <summary>
    /// Drives the Sandbox tab — a "what if" matchup simulator.
    /// User picks Team A (name + year) and Team B (name + year),
    /// then runs a cross-era projection using end-of-season ratings.
    /// Each team card mirrors the Power Rankings layout including
    /// expandable Trend/Pedigree, Season Arc, and Offense/Defense panels.
    /// </summary>
    public class SandboxViewModel : INotifyPropertyChanged
    {
        private readonly GameDataApiService _apiService;

        // ── Team A state ──────────────────────────────────────────────────
        private TeamInfo?    _teamA;
        private int          _teamAYear;
        private TeamRanking? _teamARanking;
        private List<int>    _teamAYears = new();

        // ── Team B state ──────────────────────────────────────────────────
        private TeamInfo?    _teamB;
        private int          _teamBYear;
        private TeamRanking? _teamBRanking;
        private List<int>    _teamBYears = new();

        // ── Result state ──────────────────────────────────────────────────
        private SandboxPrediction? _prediction;
        private bool               _isBusy;
        private string             _statusMessage = "Pick two teams to simulate a matchup.";

        // ── Teams list ────────────────────────────────────────────────────
        private List<TeamInfo> _allTeams = new();

        public SandboxViewModel(GameDataApiService apiService)
        {
            _apiService = apiService;

            SelectTeamACommand = new Command(async () => await SelectTeamAsync(isTeamA: true));
            SelectTeamBCommand = new Command(async () => await SelectTeamAsync(isTeamA: false));
            SelectYearACommand = new Command(async () => await SelectYearAsync(isTeamA: true));
            SelectYearBCommand = new Command(async () => await SelectYearAsync(isTeamA: false));
            RunMatchupCommand  = new Command(async () => await RunMatchupAsync(),
                                             () => CanRunMatchup);

            ToggleTrendACommand = new Command(() => ToggleTrend(isTeamA: true));
            ToggleTrendBCommand = new Command(() => ToggleTrend(isTeamA: false));
            ToggleArcACommand   = new Command(async () => await ToggleArcAsync(isTeamA: true));
            ToggleArcBCommand   = new Command(async () => await ToggleArcAsync(isTeamA: false));
            ToggleStatsACommand = new Command(() => ToggleStats(isTeamA: true));
            ToggleStatsBCommand = new Command(() => ToggleStats(isTeamA: false));

            LoadTeamsCommand = new Command(async () => await LoadTeamsAsync());
        }

        // ── Bindable properties ───────────────────────────────────────────

        public bool IsBusy
        {
            get => _isBusy;
            set { _isBusy = value; OnPropertyChanged(); }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        // Team A
        public TeamInfo? TeamA
        {
            get => _teamA;
            set { _teamA = value; OnPropertyChanged(); OnPropertyChanged(nameof(TeamALabel)); OnPropertyChanged(nameof(CanRunMatchup)); ((Command)RunMatchupCommand).ChangeCanExecute(); }
        }
        public int TeamAYear
        {
            get => _teamAYear;
            set { _teamAYear = value; OnPropertyChanged(); OnPropertyChanged(nameof(TeamALabel)); OnPropertyChanged(nameof(CanRunMatchup)); ((Command)RunMatchupCommand).ChangeCanExecute(); }
        }
        public TeamRanking? TeamARanking
        {
            get => _teamARanking;
            set { _teamARanking = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasTeamA)); }
        }
        public string TeamALabel => TeamA != null
            ? (TeamAYear > 0 ? $"{TeamA.TeamName} ({TeamAYear})" : TeamA.TeamName)
            : "Select Team A";
        public bool HasTeamA => TeamARanking != null;

        // Team B
        public TeamInfo? TeamB
        {
            get => _teamB;
            set { _teamB = value; OnPropertyChanged(); OnPropertyChanged(nameof(TeamBLabel)); OnPropertyChanged(nameof(CanRunMatchup)); ((Command)RunMatchupCommand).ChangeCanExecute(); }
        }
        public int TeamBYear
        {
            get => _teamBYear;
            set { _teamBYear = value; OnPropertyChanged(); OnPropertyChanged(nameof(TeamBLabel)); OnPropertyChanged(nameof(CanRunMatchup)); ((Command)RunMatchupCommand).ChangeCanExecute(); }
        }
        public TeamRanking? TeamBRanking
        {
            get => _teamBRanking;
            set { _teamBRanking = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasTeamB)); }
        }
        public string TeamBLabel => TeamB != null
            ? (TeamBYear > 0 ? $"{TeamB.TeamName} ({TeamBYear})" : TeamB.TeamName)
            : "Select Team B";
        public bool HasTeamB => TeamBRanking != null;

        // Prediction
        public SandboxPrediction? Prediction
        {
            get => _prediction;
            set { _prediction = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasPrediction)); }
        }
        public bool HasPrediction  => _prediction != null;
        public bool CanRunMatchup  => TeamA != null && TeamAYear > 0 && TeamB != null && TeamBYear > 0;

        // ── Commands ──────────────────────────────────────────────────────

        public ICommand SelectTeamACommand  { get; }
        public ICommand SelectTeamBCommand  { get; }
        public ICommand SelectYearACommand  { get; }
        public ICommand SelectYearBCommand  { get; }
        public ICommand RunMatchupCommand   { get; }
        public ICommand LoadTeamsCommand    { get; }
        public ICommand ToggleTrendACommand { get; }
        public ICommand ToggleTrendBCommand { get; }
        public ICommand ToggleArcACommand   { get; }
        public ICommand ToggleArcBCommand   { get; }
        public ICommand ToggleStatsACommand { get; }
        public ICommand ToggleStatsBCommand { get; }

        // ── Team selection ────────────────────────────────────────────────

        private async Task LoadTeamsAsync()
        {
            if (_allTeams.Any()) return;
            var teams = await _apiService.GetTeamsAsync();
            if (teams != null)
                _allTeams = teams.OrderBy(t => t.TeamName).ToList();
        }

        private async Task SelectTeamAsync(bool isTeamA)
        {
            await LoadTeamsAsync();
            if (!_allTeams.Any()) return;

            var options = _allTeams.Select(t => t.TeamName).ToArray();
            var result  = await Shell.Current.DisplayActionSheet(
                isTeamA ? "Select Team A" : "Select Team B",
                "Cancel", null, options);

            if (result == null || result == "Cancel") return;

            var selected = _allTeams.FirstOrDefault(t => t.TeamName == result);
            if (selected == null) return;

            // Load available years for this team
            var years = await _apiService.GetTeamAvailableYearsAsync(selected.TeamID);

            if (isTeamA)
            {
                TeamA      = selected;
                TeamAYear  = 0;
                TeamARanking = null;
                Prediction = null;
                _teamAYears = years ?? new List<int>();
                // Auto-pick year if only one available
                if (_teamAYears.Count == 1) TeamAYear = _teamAYears[0];
            }
            else
            {
                TeamB      = selected;
                TeamBYear  = 0;
                TeamBRanking = null;
                Prediction = null;
                _teamBYears = years ?? new List<int>();
                if (_teamBYears.Count == 1) TeamBYear = _teamBYears[0];
            }
        }

        private async Task SelectYearAsync(bool isTeamA)
        {
            var years = isTeamA ? _teamAYears : _teamBYears;
            if (!years.Any()) return;

            var options = years.Select(y => y.ToString()).ToArray();
            var result  = await Shell.Current.DisplayActionSheet(
                isTeamA ? "Select Year (Team A)" : "Select Year (Team B)",
                "Cancel", null, options);

            if (result == null || result == "Cancel") return;
            if (!int.TryParse(result, out var year)) return;

            if (isTeamA) TeamAYear = year;
            else         TeamBYear = year;
        }

        // ── Run matchup ───────────────────────────────────────────────────

        private async Task RunMatchupAsync()
        {
            if (!CanRunMatchup) return;
            IsBusy = true;
            StatusMessage = $"Simulating {TeamA!.TeamName} ({TeamAYear}) vs {TeamB!.TeamName} ({TeamBYear})...";
            Prediction = null;

            try
            {
                // Run prediction and both ranking loads in parallel
                // Fire prediction and season arcs in parallel.
                // Arc data gives us the max week for each year — needed to hit
                // the fully-populated weekly rankings path instead of the
                // null-stats year-end path.
                var predTask = _apiService.GetSandboxPredictionAsync(
                    TeamA.TeamName, TeamAYear, TeamB.TeamName, TeamBYear);
                var arcATask = _apiService.GetTeamSeasonArcAsync(TeamA.TeamID, TeamAYear);
                var arcBTask = _apiService.GetTeamSeasonArcAsync(TeamB.TeamID, TeamBYear);

                await Task.WhenAll(predTask, arcATask, arcBTask);

                Prediction = predTask.Result;

                var arcA     = arcATask.Result;
                var arcB     = arcBTask.Result;
                var maxWeekA = arcA?.Weeks?.Select(w => w.Week).DefaultIfEmpty(0).Max();
                var maxWeekB = arcB?.Weeks?.Select(w => w.Week).DefaultIfEmpty(0).Max();

                var rankATask = _apiService.GetPowerRankingsAsync(TeamAYear, maxWeekA > 0 ? maxWeekA : null);
                var rankBTask = _apiService.GetPowerRankingsAsync(TeamBYear, maxWeekB > 0 ? maxWeekB : null);

                await Task.WhenAll(rankATask, rankBTask);

                TeamARanking = rankATask.Result?.FirstOrDefault(r => r.TeamID == TeamA.TeamID);
                TeamBRanking = rankBTask.Result?.FirstOrDefault(r => r.TeamID == TeamB.TeamID);

                // Pre-populate arc data so the toggle panel doesn't need a reload
                if (TeamARanking != null && arcA?.Weeks?.Count > 0)
                    TeamARanking.SeasonArcWeeks = arcA.Weeks;
                if (TeamBRanking != null && arcB?.Weeks?.Count > 0)
                    TeamBRanking.SeasonArcWeeks = arcB.Weeks;

                StatusMessage = Prediction != null
                    ? "Neutral site · End-of-season ratings"
                    : "Prediction unavailable — check team/year selection.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally { IsBusy = false; }
        }

        // ── Expandable panel toggles ──────────────────────────────────────

        private void ToggleTrend(bool isTeamA)
        {
            var ranking = isTeamA ? TeamARanking : TeamBRanking;
            if (ranking == null) return;
            // TrendHistory/PedigreeHistory already populated by GetPowerRankingsAsync
            ranking.IsTrendExpanded = !ranking.IsTrendExpanded;
        }

        private async Task ToggleArcAsync(bool isTeamA)
        {
            var ranking = isTeamA ? TeamARanking : TeamBRanking;
            var team    = isTeamA ? TeamA        : TeamB;
            var year    = isTeamA ? TeamAYear    : TeamBYear;
            if (ranking == null || team == null) return;

            if (!ranking.IsArcExpanded && ranking.SeasonArcWeeks == null)
            {
                var data = await _apiService.GetTeamSeasonArcAsync(team.TeamID, year);
                if (data?.Weeks?.Count > 0)
                    ranking.SeasonArcWeeks = data.Weeks;
            }
            ranking.IsArcExpanded = !ranking.IsArcExpanded;
        }

        private void ToggleStats(bool isTeamA)
        {
            var ranking = isTeamA ? TeamARanking : TeamBRanking;
            if (ranking == null) return;
            ranking.IsStatsExpanded = !ranking.IsStatsExpanded;
        }

        // ── INotifyPropertyChanged ────────────────────────────────────────

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
