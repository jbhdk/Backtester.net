using System.Collections.Generic;

namespace Backtester.Report
{
    /// <summary>
    /// A strategy-exposed indicator series shaped for the chart library: its display name, the pane it
    /// is drawn in (as a page-friendly string), and its time-aligned points. A pre-derived projection
    /// of a <c>Backtester.Core.IndicatorSeries</c> — the page renders it without re-deriving anything.
    /// </summary>
    public class ChartIndicator
    {
        /// <summary>Gets or sets the display name of the series (e.g. <c>"SMA(20)"</c>).</summary>
        public string Name { get; set; }

        /// <summary>Gets or sets the symbol this series belongs to, or <c>null</c> to draw it on every symbol's chart.</summary>
        public string Symbol { get; set; }

        /// <summary>
        /// Gets or sets the pane placement as a page-friendly string: <c>"priceOverlay"</c> to draw on
        /// the price pane, or <c>"separatePane"</c> to draw in its own sub-pane below.
        /// </summary>
        public string Pane { get; set; }

        /// <summary>Gets or sets the time-aligned points making up the series.</summary>
        public IReadOnlyList<ChartLinePoint> Points { get; set; }
    }
}
