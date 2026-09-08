using System.Globalization;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Controls;

namespace SaturdayPulse.Converters
{
    /// <summary>
    /// Returns alternating background color for odd-numbered list rows.
    /// Theme is cached in static fields — avoids 136+ platform API calls per
    /// CollectionView render pass — and refreshed via RefreshTheme(), which
    /// is now wired to Application.Current.RequestedThemeChanged in the
    /// static constructor so it actually fires: on an OS-level theme change
    /// (AppTheme="System" in Settings) AND on SettingsViewModel.AppTheme
    /// setting Application.Current.UserAppTheme directly (Light/Dark).
    ///
    /// FIXED 2026-09: was reading AppInfo.RequestedTheme — the raw OS theme,
    /// which ignores UserAppTheme entirely — instead of
    /// Application.Current.RequestedTheme, the resolved effective theme.
    /// That meant the in-app Light/Dark/System toggle had zero effect on
    /// these row colors regardless of what was selected.
    /// </summary>
    public class OddRowBackgroundConverter : IValueConverter
    {
        private static Color _oddColor  = Color.FromArgb("#FFF8F0");
        private static Color _evenColor = Colors.White;

        static OddRowBackgroundConverter()
        {
            RefreshTheme();

            if (Application.Current != null)
                Application.Current.RequestedThemeChanged += (_, _) => RefreshTheme();
        }

        public static void RefreshTheme()
        {
            var isDark = (Application.Current?.RequestedTheme ?? AppTheme.Unspecified) == AppTheme.Dark;
            _oddColor  = isDark ? Color.FromArgb("#2A2A2A") : Color.FromArgb("#FFF8F0");
            _evenColor = isDark ? Color.FromArgb("#1A1A1A") : Colors.White;
        }

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is bool isOdd && isOdd ? _oddColor : _evenColor;

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
