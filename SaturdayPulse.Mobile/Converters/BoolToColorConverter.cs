using System.Globalization;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Controls;

namespace SaturdayPulse.Converters
{
    /// <summary>
    /// Converts boolean to color for Top 25 highlighting.
    /// Colors cached as static fields to avoid repeated allocation.
    /// </summary>
    public class BoolToColorConverter : IValueConverter
    {
        private static readonly Color Top25Color   = Color.FromArgb("#BF5700");
        private static readonly Color DefaultColor = Color.FromArgb("#808080");

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is bool isTop25 && isTop25 ? Top25Color : DefaultColor;

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
