using System.Collections.Generic;

namespace Backtester.Report
{
    /// <summary>
    /// The chart-ready section of the report model: per-symbol candle series (timestamps encoded as
    /// UTC seconds) the page draws as candlesticks. All values are pre-derived so the page only
    /// renders them.
    /// </summary>
    public class ReportChart
    {
        /// <summary>
        /// Gets or sets the candle series the run executed on, keyed by symbol.
        /// Key: symbol/ticker (string) -> the chart-ready candle series for that symbol.
        /// </summary>
        public IReadOnlyDictionary<string, IReadOnlyList<ChartCandle>> Series { get; set; }

        /// <summary>Gets or sets the entry/exit markers, each tagged with the symbol it belongs to.</summary>
        public IReadOnlyList<ChartMarker> Markers { get; set; }

        /// <summary>
        /// Gets or sets the per-round-trip stop-loss and take-profit lines, each a stepped level series
        /// confined to its round trip's holding window. Empty when the run used no brackets.
        /// </summary>
        public IReadOnlyList<ChartBracketLevel> BracketLevels { get; set; }
    }
}
