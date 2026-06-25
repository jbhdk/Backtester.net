using System.Collections.Generic;

namespace Backtester.Report
{
    /// <summary>
    /// A single line within an indicator, shaped for the chart library: its display name, its shape as
    /// a page-friendly string (<c>"line"</c>, <c>"area"</c>, or <c>"histogram"</c>), and its
    /// time-aligned points. A pre-derived projection of a <c>Backtester.Core.IndicatorSeries</c> — the
    /// page renders it without re-deriving anything.
    /// </summary>
    public class ChartIndicatorSeries
    {
        /// <summary>Gets or sets the display name of the series (e.g. <c>"SMA(20)"</c>).</summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the shape the series is drawn as, as a page-friendly string: <c>"line"</c>,
        /// <c>"area"</c>, or <c>"histogram"</c>.
        /// </summary>
        public string Shape { get; set; }

        /// <summary>Gets or sets the time-aligned points making up the series.</summary>
        public IReadOnlyList<ChartLinePoint> Points { get; set; }
    }
}
