using System.Globalization;

namespace SaturdayPulse.Extensions
{
    public static class TypedExtensions
    {
        /// <summary>
        /// Safely converts a string to a decimal. Returns 0 if conversion fails.
        /// </summary>
        public static decimal ToDecimal(this string? value)
        {
            return decimal.TryParse(
                value,
                NumberStyles.Number | NumberStyles.AllowCurrencySymbol,
                CultureInfo.InvariantCulture,
                out var result)
                ? result
                : 0m;
        }
    }
}
    