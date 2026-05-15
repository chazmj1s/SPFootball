using Syncfusion.Licensing;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SaturdayPulse.Models
{
    [Preserve(AllMembers = true)]
    public class GameResult : INotifyPropertyChanged
    {
        public int Id { get; set; }
        public int Year { get; set; }
        public int Week { get; set; }

        // Date and day from server
        public string? GameDate { get; set; }
        public string? GameDay { get; set; }

        /// <summary>Sequential position assigned by the ViewModel after load — used for "original order" sort.</summary>
        public int SequenceNumber { get; set; }

        /// <summary>True for odd sequence numbers — drives alternating row background.</summary>
        public bool IsOddRow => SequenceNumber % 2 == 1;

        /// <summary>True if the game has been played.</summary>
        public bool IsPlayed => WPoints > 0 || LPoints > 0;

        /// <summary>True if winner is the home team.</summary>
        public bool HomeIsWinner => Location == 'W';

        /// <summary>True if neutral site game.</summary>
        public bool NeutralSite => Location == 'N';

        public string WinnerName { get; set; } = string.Empty;
        public string WinnerShortName { get; set; } = string.Empty;
        public int WinnerId { get; set; }
        public string WinnerConf { get; set; } = string.Empty;
        public string WinnerTier { get; set; } = string.Empty;
        public int WPoints { get; set; }

        public string LoserName { get; set; } = string.Empty;
        public string LoserShortName { get; set; } = string.Empty;
        public int LoserId { get; set; }
        public string LoserConf { get; set; } = string.Empty;
        public string LoserTier { get; set; } = string.Empty;
        public int LPoints { get; set; }

        public char Location { get; set; }

        public int ActualOU { get; set; }
        public double? ProjWinnerScore { get; set; }
        public double? ProjLoserScore { get; set; }
        public double? ProjOU { get; set; }
        
        // --- Actual display ---
        public string Score => $"{WPoints}-{LPoints}";
        public int ActualMargin => WPoints - LPoints;
        public string DisplayOUold => $"{ActualOU}";

        // --- Projected display (all rounded to nearest integer) ---
        public bool HasProjection => ProjWinnerScore.HasValue && ProjLoserScore.HasValue;
        public string ProjScore => HasProjection
            ? $"{(int)Math.Round(ProjWinnerScore!.Value)}-{(int)Math.Round(ProjLoserScore!.Value)}"
            : "–";
        public string DisplayProjMargin => HasProjection
            ? $"{(int)Math.Round(ProjWinnerScore!.Value - ProjLoserScore!.Value)}"
            : "–";
        public string DisplayProjOU => ProjOU.HasValue ? $"{(int)Math.Round(ProjOU.Value)}" : "–";

        // --- Home/visitor display (home team on bottom per sports convention) ---
        /// <summary>The visiting team name for display (top row).</summary>
        public string VisitorName => HomeIsWinner
            ? (!string.IsNullOrEmpty(LoserShortName) ? LoserShortName : LoserName)
            : (!string.IsNullOrEmpty(WinnerShortName) ? WinnerShortName : WinnerName);

        /// <summary>The home team name for display (bottom row).</summary>
        public string HomeName => HomeIsWinner
            ? (!string.IsNullOrEmpty(WinnerShortName) ? WinnerShortName : WinnerName)
            : (!string.IsNullOrEmpty(LoserShortName) ? LoserShortName : LoserName);

        /// <summary>Visitor score for display.</summary>
        public string VisitorScore => IsPlayed
            ? (HomeIsWinner ? LPoints.ToString() : WPoints.ToString())
            : "–";

        /// <summary>Home score for display.</summary>
        public string HomeScore => IsPlayed
            ? (HomeIsWinner ? WPoints.ToString() : LPoints.ToString())
            : "–";

        public string ProjVisitorScore => HomeIsWinner
            ? (ProjLoserScore.HasValue ? $"{(int)Math.Round(ProjLoserScore.Value)}" : "–")
            : (ProjWinnerScore.HasValue ? $"{(int)Math.Round(ProjWinnerScore.Value)}" : "–");

        public string ProjHomeScore => HomeIsWinner
            ? (ProjWinnerScore.HasValue ? $"{(int)Math.Round(ProjWinnerScore.Value)}" : "–")
            : (ProjLoserScore.HasValue ? $"{(int)Math.Round(ProjLoserScore.Value)}" : "–");
        // Visitor score: "20 (18)" or "(18)" if unplayed
        public string DisplayVisitorScore => IsPlayed
            ? $"{VisitorScore} ({ProjVisitorScore})"
            : $"({ProjVisitorScore})";

        // Home score: "23 (35)" or "(35)" if unplayed  
        public string DisplayHomeScore => IsPlayed
            ? $"{HomeScore} ({ProjHomeScore})"
            : $"({ProjHomeScore})";

        // Margin on visitor row: "margin: 3 (-9)" or "margin: (-9)"
        public string DisplayMargin => IsPlayed
            ? $"margin: {ActualMargin} ({DisplayProjMargin})"
            : $"margin: ({DisplayProjMargin})";

        // O/U on home row: "O/U: 43 (53)" or "O/U: (53)"
        public string DisplayOU => IsPlayed
            ? $"O/U: {ActualOU} ({DisplayProjOU})"
            : $"O/U: ({DisplayProjOU})";

        /// <summary>Visitor team ID for follow/star.</summary>
        public int VisitorId => HomeIsWinner ? LoserId : WinnerId;

        /// <summary>Home team ID for follow/star.</summary>
        public int HomeId => HomeIsWinner ? WinnerId : LoserId;

        /// <summary>Visitor conference.</summary>
        public string VisitorConf => HomeIsWinner ? LoserConf : WinnerConf;

        /// <summary>Home conference.</summary>
        public string HomeConf => HomeIsWinner ? WinnerConf : LoserConf;

        /// <summary>
        /// Group header for date-based grouping — e.g. "Sat, Aug 23"
        /// Drops the year since the user is already filtered to a specific season.
        /// </summary>
        public string GroupHeader
        {
            get
            {
                if (string.IsNullOrEmpty(GameDay) || string.IsNullOrEmpty(GameDate))
                    return $"Week {Week}";

                // GameDate format: "Aug 23 2025" or "Sep 1 2025"
                // Split on space and take first two parts for "Aug 23" or "Sep 1"
                var parts = GameDate.Split(' ');
                var monthDay = parts.Length >= 2 ? $"{parts[0]} {parts[1]}" : GameDate;
                return $"{GameDay}, {monthDay}";
            }
        }

        /// <summary>Neutral site indicator for display.</summary>
        public string NeutralIndicator => NeutralSite ? " (N)" : string.Empty;

        private bool _showGroupHeader;
        public bool ShowGroupHeader
        {
            get => _showGroupHeader;
            set { _showGroupHeader = value; OnPropertyChanged(); }
        }

        // --- Follow state ---
        private bool _winnerIsFollowed;
        public bool WinnerIsFollowed
        {
            get => _winnerIsFollowed;
            set { _winnerIsFollowed = value; OnPropertyChanged(); OnPropertyChanged(nameof(VisitorIsFollowed)); OnPropertyChanged(nameof(HomeIsFollowed)); }
        }

        private bool _loserIsFollowed;
        public bool LoserIsFollowed
        {
            get => _loserIsFollowed;
            set { _loserIsFollowed = value; OnPropertyChanged(); OnPropertyChanged(nameof(VisitorIsFollowed)); OnPropertyChanged(nameof(HomeIsFollowed)); }
        }
        private bool _isPersonalFollowed;
        public bool IsGameFavorited
        {
            get => _isPersonalFollowed;
            set { _isPersonalFollowed = value; OnPropertyChanged(); }
        }

        /// <summary>Follow state for visitor (top row).</summary>
        public bool VisitorIsFollowed => HomeIsWinner ? LoserIsFollowed : WinnerIsFollowed;

        /// <summary>Follow state for home (bottom row).</summary>
        public bool HomeIsFollowed => HomeIsWinner ? WinnerIsFollowed : LoserIsFollowed;

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}