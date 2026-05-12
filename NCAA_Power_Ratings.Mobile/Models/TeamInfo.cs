using Syncfusion.Licensing;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NCAA_Power_Ratings.Mobile.Models
{
    [Preserve(AllMembers = true)]
    public class TeamInfo : INotifyPropertyChanged
    {
        public int TeamID { get; set; }
        public string TeamName { get; set; } = string.Empty;
        public string ShortName { get; set; } = string.Empty;
        public string? Conference { get; set; }
        public string? ConferenceAbbr { get; set; }
        public string? Division { get; set; }
        public string Tier { get; set; } = string.Empty;

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
