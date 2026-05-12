using Syncfusion.Licensing;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SaturdayPulse.Models
{
    /// <summary>
    /// Represents a named rivalry between two teams, including series metadata.
    /// Maps to the /api/productiongamedata/rivalries/named endpoint response.
    /// </summary>
    [Preserve(AllMembers = true)]
    public class RivalryInfo : INotifyPropertyChanged
    {
        public int Team1Id { get; set; }
        public string Team1Name { get; set; } = string.Empty;
        public string Team1ShortName { get; set; } = string.Empty;

        public int Team2Id { get; set; }
        public string Team2Name { get; set; } = string.Empty;
        public string Team2ShortName { get; set; } = string.Empty;

        public string? RivalryName { get; set; }
        public string? RivalryTier { get; set; }

        public int GamesPlayed { get; set; }
        public decimal AvgMargin { get; set; }
        public decimal StDevMargin { get; set; }
        public decimal UpsetRate { get; set; }
        public int FirstPlayed { get; set; }
        public int LastPlayed { get; set; }
        public bool IsPersonalFollowed { get; set; }

        // Derived display helpers
        public string DisplayMatchup => $"{Team1ShortName} vs {Team2ShortName}";
        public string DisplaySeries => $"{GamesPlayed} games ({FirstPlayed}–{LastPlayed})";
        public string DisplayUpsetRate => $"{UpsetRate:P0} upset rate";
        public string DisplayAvgMargin => $"avg margin: {AvgMargin:F1}";
        public string DisplayAvgSwing => $"avg swing: {StDevMargin:F1}";
        public string TierBadge => RivalryTier switch
        {
            "EPIC" => "🔥",
            "NATIONAL" => "⭐",
            "REGIONAL" => "🏠",
            "MEH" => "•",
            _ => ""
        };

        // Follow state for Team 1
        private bool _team1IsFollowed;
        public bool Team1IsFollowed
        {
            get => _team1IsFollowed;
            set { _team1IsFollowed = value; OnPropertyChanged(); }
        }

        // Follow state for Team 2
        private bool _team2IsFollowed;
        public bool Team2IsFollowed
        {
            get => _team2IsFollowed;
            set { _team2IsFollowed = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}