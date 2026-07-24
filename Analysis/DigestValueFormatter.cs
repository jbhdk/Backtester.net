using System;
using System.Globalization;

namespace Backtester.Analysis
{
    /// <summary>
    /// Formats digest values exactly as the report page displays them, so a Finding quotes a figure the
    /// reader can find on the page. Raw round-trip decimal precision is never sent.
    /// </summary>
    internal static class DigestValueFormatter
    {
        /// <summary>Formats a currency amount as the report does, e.g. <c>"$1,234.56"</c> or <c>"-$450.50"</c>.</summary>
        public static string Money(decimal value)
        {
            string magnitude = Math.Abs(value).ToString("#,##0.00", CultureInfo.InvariantCulture);
            return value < 0 ? "-$" + magnitude : "$" + magnitude;
        }

        /// <summary>Formats a fraction as the report does: scaled to a percentage with two decimals, e.g. <c>"12.35%"</c>.</summary>
        public static string Percent(decimal fraction)
        {
            return (fraction * 100m).ToString("0.00", CultureInfo.InvariantCulture) + "%";
        }

        /// <summary>Formats a plain ratio or count as the report does: two decimals, e.g. <c>"1.62"</c>.</summary>
        public static string Number(decimal value)
        {
            return value.ToString("0.00", CultureInfo.InvariantCulture);
        }

        /// <summary>Formats an optional ratio as the report does: two decimals, or the report's en-dash when absent.</summary>
        public static string Number(decimal? value)
        {
            return value.HasValue ? Number(value.Value) : "–";
        }

        /// <summary>Formats a UTC timestamp as the report does, e.g. <c>"2024-01-02 14:30Z"</c>.</summary>
        public static string Timestamp(DateTime value)
        {
            return value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + "Z";
        }
    }
}
