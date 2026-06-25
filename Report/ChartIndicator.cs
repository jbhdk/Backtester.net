using System.Collections.Generic;

namespace Backtester.Report
{
    /// <summary>
    /// A strategy-exposed composite indicator shaped for the chart library: its display name, the
    /// symbol it is scoped to, the pane it occupies (as a page-friendly string), and the ordered series
    /// drawn within that pane. A pre-derived projection of a <c>Backtester.Core.Indicator</c> — the page
    /// renders it without re-deriving anything.
    /// </summary>
    public class ChartIndicator
    {
        /// <summary>Gets or sets the display name of the indicator (e.g. <c>"MACD"</c> or <c>"SMA(20)"</c>).</summary>
        public string Name { get; set; }

        /// <summary>Gets or sets the symbol this indicator belongs to, or <c>null</c> to draw it on every symbol's chart.</summary>
        public string Symbol { get; set; }

        /// <summary>
        /// Gets or sets the pane placement as a page-friendly string: <c>"priceOverlay"</c> to draw on
        /// the price pane, or <c>"separatePane"</c> to draw in its own sub-pane below.
        /// </summary>
        public string Pane { get; set; }

        /// <summary>Gets or sets the ordered series drawn within this indicator's pane.</summary>
        public IReadOnlyList<ChartIndicatorSeries> Series { get; set; }
    }
}
