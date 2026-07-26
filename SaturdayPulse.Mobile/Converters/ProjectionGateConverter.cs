using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Maui.Controls;

namespace SaturdayPulse.Converters
{
    /// <summary>
    /// Season Pass gating (2026-07-25, revised 2026-07-26) for game-card
    /// projections. GameResult's DisplayVisitorScore/DisplayHomeScore/
    /// DisplayMargin/DisplayOU always put the projected number in
    /// parentheses — "24 (28)" when played, "(28)" alone when not — so a
    /// free user's view swaps every "(...)" group for a lock glyph rather
    /// than showing the projection.
    ///
    /// Deliberately a global regex replace, not "find the first paren and
    /// split on it": MyTeamsGameRow.ScoreLine is already a combined
    /// "{Home} - {Away}" string built in MyTeamsViewModel.BuildGameRow, which
    /// can contain TWO parenthetical groups in one bound string. Replacing
    /// every match handles that the same way it handles Schedule's
    /// single-group Spread/O-U strings — no GameResult.cs changes needed.
    ///
    /// 2026-07-26: free users get last year's projections unlocked too (only
    /// the paywall-gated stuff — Trend/Pedigree, Season Arc, Vegas details,
    /// Postseason/Sandbox — stays behind Season Pass). So this only masks
    /// when the displayed game's Year is the current season; anything
    /// earlier (last year, or any year a paid user is browsing) passes
    /// through untouched.
    ///
    /// Takes three bound values via MultiBinding: [0] the raw display
    /// string, [1] the page's HasSeasonPass, [2] the game's Year (bound
    /// directly to GameResult.Year for Schedule, via Game.Year for
    /// MyTeamsGameRow). Entitled users always get the string unchanged.
    /// </summary>
    public class ProjectionGateConverter : IMultiValueConverter
    {
        private static readonly Regex ParenGroup = new(@"\([^)]*\)", RegexOptions.Compiled);
        private const string LockGlyph = "(🔒)";

        public object? Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
        {
            var text = values.Length > 0 ? values[0] as string : null;
            if (string.IsNullOrEmpty(text)) return text ?? string.Empty;

            var hasSeasonPass = values.Length > 1 && values[1] is bool b && b;
            if (hasSeasonPass) return text;

            // Free users see last year's (and any earlier year's) projections
            // unmasked — only the live/current season is locked. Defaults to
            // "current year" (i.e. locked) if the year value is missing,
            // since that's the safer failure mode.
            var gameYear = values.Length > 2 && values[2] is int y ? y : DateTime.Now.Year;
            if (gameYear < DateTime.Now.Year) return text;

            return ParenGroup.Replace(text, LockGlyph);
        }

        public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
            => throw new NotSupportedException("ProjectionGateConverter is one-way — game display strings are never written back.");
    }
}
