using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SaturdayPulse.Helpers;
using SaturdayPulse.Models;
using SaturdayPulse.Services;

namespace SaturdayPulse.ViewModels
{
    public class TeamsViewModel : BaseViewModel
    {
        private readonly GameDataApiService _apiService;
        private List<TeamInfo> _allTeams = [];
        private ObservableCollection<TeamInfo> _teams = [];

        public ObservableCollection<TeamInfo> Teams
        {
            get => _teams;
            private set { _teams = value; OnPropertyChanged(); }
        }

        private ObservableCollection<string> _conferenceFilters = [];
        public ObservableCollection<string> ConferenceFilters
        {
            get => _conferenceFilters;
            private set { _conferenceFilters = value; OnPropertyChanged(); }
        }

        private string _selectedConference = "All";
        public string SelectedConference
        {
            get => _selectedConference;
            set
            {
                if (_selectedConference == value) return;
                _selectedConference = value;
                OnPropertyChanged();
                ApplyFilter();
            }
        }

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set { _isLoading = value; OnPropertyChanged(); }
        }

        private string _statusMessage = string.Empty;
        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }
        
        public bool HasLoaded { get; private set; }

        public TeamsViewModel(GameDataApiService apiService, FollowService followService)
    : base(followService)
        {
            _apiService = apiService;

            // React to follow changes from any tab
            _followService.TeamFollowChanged += OnTeamFollowChanged;
        }

        public async Task LoadDataAsync()
        {
            IsLoading = true;
            StatusMessage = string.Empty;
            try
            {
                var teams = await _apiService.GetTeamsAsync();
                if (teams == null || teams.Count == 0)
                {
                    StatusMessage = "No teams available.";
                    return;
                }

                // Hydrate follow state from FollowService
                foreach (var t in teams)
                    t.IsFollowed = _followService.IsFollowed(t.TeamID);

                _allTeams = [.. teams.OrderBy(t => t.TeamName)];
                ConferenceFilters = new ObservableCollection<string>(ConferenceHelper.FilterDisplayList());
                _selectedConference = "All";
                OnPropertyChanged(nameof(SelectedConference));

                ApplyFilter();
                HasLoaded = true;

            }
            catch (Exception ex)
            {
                StatusMessage = $"Error loading teams: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void ApplyFilter()
        {
            var filtered = _allTeams.AsEnumerable();

            if (SelectedConference != "All")
            {
                var abbr = ConferenceHelper.DisplayToAbbr(SelectedConference);
                filtered = filtered.Where(t => t.ConferenceAbbr == abbr);
            }

            // Followed teams first, then alphabetical within each group
            var sorted = filtered
                .OrderByDescending(t => t.IsFollowed)
                .ThenBy(t => t.TeamName);

            Teams = new ObservableCollection<TeamInfo>(sorted);
        }

        private void ToggleFollow(TeamInfo? team)
        {
            if (team == null) return;
            // FollowService fires TeamFollowChanged, which calls OnTeamFollowChanged
            _followService.Toggle(team.TeamID);
        }

        private void OnTeamFollowChanged(int teamId, bool isFollowed)
        {
            // Update the TeamInfo object in our list
            var team = _allTeams.FirstOrDefault(t => t.TeamID == teamId);
            if (team != null)
            {
                team.IsFollowed = isFollowed;
                // Re-sort so followed teams bubble to the top
                ApplyFilter();
            }
        }
    }
}