using System.Collections.ObjectModel;
using System.Windows.Input;
using SaturdayPulse.Models;
using SaturdayPulse.Services;

namespace SaturdayPulse.ViewModels
{
    public class RivalriesViewModel : BaseViewModel
    {
        private readonly GameDataApiService _apiService;
        private List<RivalryInfo> _allRivalries = [];
        private ObservableCollection<RivalryInfo> _rivalries = [];

        public ObservableCollection<RivalryInfo> Rivalries
        {
            get => _rivalries;
            private set { _rivalries = value; OnPropertyChanged(); }
        }

        // Tier filter options
        public List<string> TierFilters { get; } =
            ["All", "🔥 Epic", "⭐ National", "🏠 Regional", "• Meh"];

        private string _selectedTier = "All";
        public string SelectedTier
        {
            get => _selectedTier;
            set
            {
                if (_selectedTier == value) return;
                _selectedTier = value;
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

        public RivalriesViewModel(GameDataApiService apiService, FollowService followService)
            : base(followService)
        {
            _apiService = apiService;
            _followService.TeamFollowChanged += OnTeamFollowChanged;
        }

        public async Task LoadDataAsync()
        {
            if (IsLoading) return;
            IsLoading = true;
            StatusMessage = string.Empty;

            try
            {
                var rivalries = await _apiService.GetNamedRivalriesAsync();

                if (rivalries == null || rivalries.Count == 0)
                {
                    StatusMessage = "No rivalries available.";
                    return;
                }

                // Hydrate follow state for both teams in each rivalry
                var followedIds = _followService.GetFollowedIds();
                foreach (var r in rivalries)
                {
                    r.Team1IsFollowed = followedIds.Contains(r.Team1Id);
                    r.Team2IsFollowed = followedIds.Contains(r.Team2Id);
                }

                // Sort: EPIC first, then NATIONAL, STATE, MEH, alphabetical within tier
                _allRivalries = [.. rivalries.OrderBy(r => TierSortOrder(r.RivalryTier))
                                             .ThenBy(r => r.RivalryName)];

                ApplyFilter();
                
                HasLoaded = true;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error loading rivalries: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void ApplyFilter()
        {
            var filtered = _allRivalries.AsEnumerable();

            filtered = _selectedTier switch
            {
                "🔥 Epic" => filtered.Where(r => r.RivalryTier == "EPIC"),
                "⭐ National" => filtered.Where(r => r.RivalryTier == "NATIONAL"),
                "🏠 State" => filtered.Where(r => r.RivalryTier == "REGIONAL"),
                "• Meh" => filtered.Where(r => r.RivalryTier == "MEH"),
                _ => filtered   // "All"
            };

            Rivalries = new ObservableCollection<RivalryInfo>(filtered);
        }

        private void OnTeamFollowChanged(int teamId, bool isFollowed)
        {
            foreach (var r in _allRivalries)
            {
                if (r.Team1Id == teamId) r.Team1IsFollowed = isFollowed;
                if (r.Team2Id == teamId) r.Team2IsFollowed = isFollowed;
            }
            // Re-apply filter to refresh the visible collection
            ApplyFilter();
        }

        private static int TierSortOrder(string? tier) => tier switch
        {
            "EPIC" => 0,
            "NATIONAL" => 1,
            "REGIONAL" => 2,
            "MEH" => 3,
            _ => 4
        };
    }
}