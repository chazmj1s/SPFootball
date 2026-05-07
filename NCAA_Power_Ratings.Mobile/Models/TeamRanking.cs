using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NCAA_Power_Ratings.Mobile.Models
{
    /// <summary>
    /// Represents a team with power rating and ranking information
    /// </summary>
    public class TeamRanking : INotifyPropertyChanged
    {
        public int TeamID { get; set; }
        public string TeamName { get; set; } = string.Empty;
        public string? Conference { get; set; }
        public string? ConferenceAbbr { get; set; }
        public string? Division { get; set; }

        // Tier classification and ranking
        public string? Tier { get; set; }           // "P4", "G5", "Independent", "Other"
        public int OverallRank { get; set; }        // Rank among ALL teams (1-133)
        public int TierRank { get; set; }           // Rank within tier (e.g., #1 G5)
        public decimal? Ranking { get; set; }
        public int Year { get; set; }
        public byte Wins { get; set; }
        public byte Losses { get; set; }
        public decimal? BaseSOS { get; set; }
        public decimal? CombinedSOS { get; set; }

        public string Record => $"{Wins}-{Losses}";

        // Display helpers
        public string DisplayRank => $"#{OverallRank}";
        public string DisplayTierRank => $"#{TierRank}";
        public string DisplayTier => Tier ?? "N/A";
        public string DisplayTierWithRank => Tier switch
        {
            "P4" => $"P4 (#{TierRank})",
            "G5" => $"G5 (#{TierRank})",
            "Independent" => "Ind",
            _ => Tier ?? "N/A"
        };

        public string DisplayConferenceTier => $"{ConferenceAbbr} · {DisplayTierWithRank}";
        public string DisplaySOS => CombinedSOS?.ToString("F4") ?? "N/A";

        // Set by ViewModel after each sort change to drive the dynamic column
        public string ActiveSortValue { get; set; } = string.Empty;

        public bool IsTop25 { get; set; }
        public bool IsOddRow { get; set; }

        private bool _isFollowed;
        public bool IsFollowed
        {
            get => _isFollowed;
            set { _isFollowed = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
