using System.Globalization;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Controls;

namespace SaturdayPulse.Converters
{
    public class BoolToBorderColorConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool isTop25 && isTop25)
            {
                // Gold border for Top 25
                return Color.FromArgb("#FFD700");
            }

            // Light gray border for others
            return Color.FromArgb("#DDDDDD");
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}