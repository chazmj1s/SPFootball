using Syncfusion.Licensing;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SaturdayPulse.Models;

[Preserve(AllMembers = true)]
public class TeamRanking : INotifyPropertyChanged
{
    // =========================================================
    // Core Team Data
    // =========================================================

    public int TeamID { get; set; }
    public string TeamName { get; set; } = string.Empty;

    public string? Conference { get; set; }
    public string? ConferenceAbbr { get; set; }
    public string? Division { get; set; }
    public string? Tier { get; set; }

    public int Year { get; set; }

    public byte Wins { get; set; }
    public byte Losses { get; set; }

    public int OverallRank { get; set; }
    public int TierRank { get; set; }

    public decimal? Ranking { get; set; }

    // =========================================================
    // Ratings
    // =========================================================

    public decimal? BaseSOS { get; set; }
    public decimal? CombinedSOS { get; set; }

    public decimal AvgPointsScored { get; set; }
    public decimal AvgPointsAllowed { get; set; }

    public decimal OffensiveZScore { get; set; }
    public decimal DefensiveZScore { get; set; }

    public int OffensiveRank { get; set; }
    public int DefensiveRank { get; set; }

    public double TrendRating { get; set; }
    public double PedigreeRating { get; set; }
    public double SeedRating { get; set; }

    // =========================================================
    // Raw History Data
    // =========================================================

    private List<double>? _trendHistory;

    public List<double>? TrendHistory
    {
        get => _trendHistory;
        set
        {
            if (SetProperty(ref _trendHistory, value))
                OnPropertyChanged(nameof(HasTrendData));
        }
    }

    private List<double>? _pedigreeHistory;

    public List<double>? PedigreeHistory
    {
        get => _pedigreeHistory;
        set
        {
            if (SetProperty(ref _pedigreeHistory, value))
                OnPropertyChanged(nameof(HasTrendData));
        }
    }

    // =========================================================
    // Display Helpers
    // =========================================================

    public string Record => $"{Wins}-{Losses}";

    public string DisplayRank =>
        Ranking?.ToString() ?? "N/A";

    public string DisplayTier =>
        Tier ?? "N/A";

    public string DisplayTierRank =>
        $"#{TierRank}";

    public string DisplayTierWithRank => Tier switch
    {
        "P4" => $"P4 (#{TierRank})",
        "G5" => $"G5 (#{TierRank})",
        "Independent" => "Ind",
        _ => Tier ?? "N/A"
    };

    public string DisplayConferenceTier =>
        $"{ConferenceAbbr} · {DisplayTierWithRank}";

    public string DisplaySOS =>
        CombinedSOS?.ToString("F4") ?? "N/A";

    public string OffensiveRankDisplay =>
        OffensiveRank > 0 ? $"#{OffensiveRank}" : "—";

    public string DefensiveRankDisplay =>
        DefensiveRank > 0 ? $"#{DefensiveRank}" : "—";

    // =========================================================
    // UI State
    // =========================================================

    public bool IsTop25 { get; set; }
    public bool IsOddRow { get; set; }

    public string ActiveSortValue { get; set; } = string.Empty;

    // =========================================================
    // Follow State
    // =========================================================

    private bool _isFollowed;

    public bool IsFollowed
    {
        get => _isFollowed;
        set => SetProperty(ref _isFollowed, value);
    }

    // =========================================================
    // Offense / Defense Expansion
    // =========================================================

    public bool HasOffenseDefenseData =>
        AvgPointsScored > 0 && OffensiveRank > 0;

    public string StatsExpandIcon =>
        IsStatsExpanded ? "▲" : "▼";

    private bool _isStatsExpanded;

    public bool IsStatsExpanded
    {
        get => _isStatsExpanded;
        set
        {
            if (SetProperty(ref _isStatsExpanded, value))
                OnPropertyChanged(nameof(StatsExpandIcon));
        }
    }

    // =========================================================
    // Trend / Pedigree Expansion
    // =========================================================

    public bool HasTrendData =>
        TrendHistory?.Count > 0 || PedigreeHistory?.Count > 0;

    public string TrendExpandIcon =>
        IsTrendExpanded ? "▲" : "▼";

    private bool _isTrendExpanded;

    public bool IsTrendExpanded
    {
        get => _isTrendExpanded;
        set
        {
            if (!SetProperty(ref _isTrendExpanded, value)) return;
            OnPropertyChanged(nameof(TrendExpandIcon));
            OnPropertyChanged(nameof(HasTrendData));
            if (_isTrendExpanded) EnsureTrendChartDataLoaded();
        }
    }

    // =========================================================
    // Season Arc Expansion
    // =========================================================

    public bool HasArcData => SeasonArcWeeks?.Count > 0;

    public string ArcExpandIcon => IsArcExpanded ? "▲" : "▼";

    private bool _isArcExpanded;

    public bool IsArcExpanded
    {
        get => _isArcExpanded;
        set
        {
            if (!SetProperty(ref _isArcExpanded, value)) return;
            OnPropertyChanged(nameof(ArcExpandIcon));
            if (_isArcExpanded) EnsureArcChartDataLoaded();
        }
    }

    private List<TeamSeasonWeek>? _seasonArcWeeks;

    public List<TeamSeasonWeek>? SeasonArcWeeks
    {
        get => _seasonArcWeeks;
        set
        {
            if (SetProperty(ref _seasonArcWeeks, value))
            {
                OnPropertyChanged(nameof(HasArcData));
                _arcChartDataLoaded = false;
                if (_isArcExpanded) EnsureArcChartDataLoaded();
            }
        }
    }

    // =========================================================
    // Trend / Pedigree Chart Data
    // =========================================================

    public ObservableCollection<ChartPoint> TrendPoints    { get; } = new();
    public ObservableCollection<ChartPoint> PedigreePoints { get; } = new();

    private bool _trendChartDataLoaded;

    private void EnsureTrendChartDataLoaded()
    {
        if (_trendChartDataLoaded) return;
        LoadTrendChartPoints();
        _trendChartDataLoaded = true;
    }

    public void LoadTrendChartPoints()
    {
        TrendPoints.Clear();
        PedigreePoints.Clear();

        if (TrendHistory != null)
            for (int i = 0; i < TrendHistory.Count; i++)
                TrendPoints.Add(new ChartPoint { Index = i + 1, Value = TrendHistory[i] });

        if (PedigreeHistory != null)
            for (int i = 0; i < PedigreeHistory.Count; i++)
                PedigreePoints.Add(new ChartPoint { Index = i + 1, Value = PedigreeHistory[i] });

        OnPropertyChanged(nameof(HasTrendData));
    }

    // keep old name as alias so existing callers don't break
    public void LoadChartPoints() => LoadTrendChartPoints();

    // =========================================================
    // Season Arc Chart Data
    // =========================================================

    public ObservableCollection<ChartPoint> ArcRankingPoints { get; } = new();
    public ObservableCollection<ChartPoint> ArcSosPoints     { get; } = new();
    public ObservableCollection<ChartPoint> ArcWinPctPoints  { get; } = new();

    private bool _arcChartDataLoaded;

    private void EnsureArcChartDataLoaded()
    {
        if (_arcChartDataLoaded || SeasonArcWeeks == null) return;
        LoadArcChartPoints();
        _arcChartDataLoaded = true;
    }

    private void LoadArcChartPoints()
    {
        ArcRankingPoints.Clear();
        ArcSosPoints.Clear();
        ArcWinPctPoints.Clear();

        if (SeasonArcWeeks == null) return;

        foreach (var w in SeasonArcWeeks)
        {
            ArcRankingPoints.Add(new ChartPoint { Index = w.Week, Value = w.Ranking  ?? 0 });
            ArcSosPoints    .Add(new ChartPoint { Index = w.Week, Value = (w.CombinedSOS-1)*10 ?? 0 });
            ArcWinPctPoints .Add(new ChartPoint { Index = w.Week, Value = w.WinPct });
        }
    }

    // =========================================================
    // INotifyPropertyChanged
    // =========================================================

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    protected bool SetProperty<T>(
        ref T backingStore, T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(backingStore, value)) return false;
        backingStore = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
