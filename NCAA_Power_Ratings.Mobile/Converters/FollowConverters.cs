using System.Globalization;
using Microsoft.Maui.Graphics;

namespace NCAA_Power_Ratings.Mobile.Converters
{
    /// <summary>
    /// Team follow glyph: ♥ when followed, ♡ when not.
    /// </summary>
    public class FollowTeamGlyphConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is bool b && b ? "♥" : "♡";

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Team follow color: #FF6666 when followed, #666666 when not.
    /// </summary>
    public class FollowTeamColorConverter : IValueConverter
    {
        private static readonly Color On  = Color.FromArgb("#FF6666");
        private static readonly Color Off = Color.FromArgb("#666666");

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is bool b && b ? On : Off;

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Game follow glyph: ★ when followed, ☆ when not.
    /// </summary>
    public class FollowGameGlyphConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is bool b && b ? "★" : "☆";

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Game follow color: Gold when followed, #666666 when not.
    /// </summary>
    public class FollowGameColorConverter : IValueConverter
    {
        private static readonly Color On  = Colors.Gold;
        private static readonly Color Off = Color.FromArgb("#666666");

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is bool b && b ? On : Off;

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
