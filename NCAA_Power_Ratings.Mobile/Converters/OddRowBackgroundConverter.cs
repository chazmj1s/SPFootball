using System.Globalization;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Controls;

namespace NCAA_Power_Ratings.Mobile.Converters
{
    /// <summary>
    /// Returns alternating background color for odd-numbered list rows.
    /// Theme is checked once at construction and cached — avoids 136+ platform
    /// API calls per CollectionView render pass.
    /// Re-instantiate or call RefreshTheme() if the user switches theme at runtime.
    /// </summary>
    public class OddRowBackgroundConverter : IValueConverter
    {
        private static Color _oddColor  = Color.FromArgb("#FFF8F0");
        private static Color _evenColor = Colors.White;

        static OddRowBackgroundConverter()
        {
            RefreshTheme();
        }

        public static void RefreshTheme()
        {
            var isDark = AppInfo.RequestedTheme == AppTheme.Dark;
            _oddColor  = isDark ? Color.FromArgb("#2A2A2A") : Color.FromArgb("#FFF8F0");
            _evenColor = isDark ? Color.FromArgb("#1A1A1A") : Colors.White;
        }

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is bool isOdd && isOdd ? _oddColor : _evenColor;

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
