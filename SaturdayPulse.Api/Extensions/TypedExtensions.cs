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

        public static int NormalizedScore(this double score)
        {
            return (int)Math.Round(score) switch
            {
                1 or 2 or 4 => 3,
                5 => 6,
                8 => 7,
                11 => 10,
                _ => (int)Math.Round(score)
            };
        }
    }
}
    