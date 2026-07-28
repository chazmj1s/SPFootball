using System.Globalization;

namespace SaturdayPulse.Converters
{
    /// <summary>
    /// IsVisible gate for the Rivalry Notes link/panel: true only when this specific
    /// game has real curated rivalry data (RivalryNotes != null — true for the 52
    /// curated MatchupHistory pairs, null for the ~750+ others) AND the user has an
    /// active Season Pass.
    ///
    /// Deliberately different from the Details toggle's visibility rule: Details
    /// stays visible for everyone (ShowDetails is data-only), with only its inner
    /// Vegas/projections content paywalled for free users. Rivalry Notes hides
    /// entirely for free users instead — no locked-teaser state, since there's no
    /// "here's what you're missing" value in showing an empty/locked rivalry card.
    ///
    /// Bindings, in order:
    ///   [0] (Game.)RivalryNotes           (object?) — null for non-curated pairs
    ///   [1] BindingContext.HasSeasonPass  (bool)    — from the page's ViewModel
    /// </summary>
    public class RivalryNotesVisibilityConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 2)
                return false;

            var hasRivalryData = values[0] != null;
            var hasSeasonPass  = values[1] is bool b && b;

            return hasRivalryData && hasSeasonPass;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
