namespace SaturdayPulse.Models
{
    /// <summary>
    /// Maps onto the "RivalryNotes" object returned by GetScheduleV2Async /
    /// GetTeamScheduleV2Async — null for the ~750+ non-curated pairings, populated
    /// for the 52 curated MatchupHistory rivalries.
    ///
    /// Plain set-once DTO, same treatment as GameLines/GameTeamStats — assigned once
    /// when GameResult.RivalryNotes is set and never mutated afterward, so no
    /// INotifyPropertyChanged needed here (GameResult's own setter already fires the
    /// change notification for the whole object).
    /// </summary>
    public class RivalryNotes
    {
        public string RivalryName      { get; set; } = string.Empty;
        public int    FirstPlayed      { get; set; }
        public double AverageSpread    { get; set; }
        public double AverageOverUnder { get; set; }
        public double UpsetChance      { get; set; }
        public string Blurb { get; set; } = string.Empty;
        public string Series { get; set; } = string.Empty;

        // ── Display formatting — matches the Display* convention used elsewhere
        // (DisplaySpread, DisplayRating, DisplaySOS, etc.) ──────────────────────

        public string DisplayHeader => $"{RivalryName} - First played {FirstPlayed}";

        /// <summary>
        /// Rounded to the nearest half-point — same convention as
        /// GameResult.DisplayProjMargin (Math.Round(x * 2, AwayFromZero) / 2) —
        /// rather than showing the raw two-decimal average.
        /// </summary>
        public string DisplayAverageSpread
        {
            get
            {
                var rounded = Math.Round(AverageSpread * 2, MidpointRounding.AwayFromZero) / 2;
                return $"Average Margin: {rounded:F1}";
            }
        }

        public string DisplayAverageOU => $"Average O/U: {AverageOverUnder:F2}";

        /// <summary>
        /// Whole percentage (e.g. "12%"), not the raw 0-1 decimal. Built manually
        /// rather than with a :P0 format string, since P0's percent-sign spacing is
        /// locale-dependent and this needs to read exactly "12%", not "12 %".
        /// </summary>
        public string DisplayUpsetChance => $"Chance of upset: {UpsetChance * 100:F0}%";
    }
}
