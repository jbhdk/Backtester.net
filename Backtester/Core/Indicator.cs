using System.Collections.Generic;

namespace Backtester.Core
{
    /// <summary>
    /// A composite indicator a strategy exposed for visualization: a named study grouping one or more
    /// <see cref="IndicatorSeries"/> that share a single placement (the price overlay, or one separate
    /// pane all its series occupy) and an optional symbol binding (ADR 0012). A single-line indicator
    /// such as a moving average is just an Indicator with one series, so the common case stays simple.
    /// </summary>
    public class Indicator
    {
        /// <summary>
        /// Initializes a new indicator not bound to any symbol (drawn on every symbol's chart). Suitable
        /// for a single-symbol run where the symbol is unambiguous.
        /// </summary>
        public Indicator(string name, IndicatorPane pane, IReadOnlyList<IndicatorSeries> series)
            : this(name, null, pane, series)
        {
        }

        /// <summary>
        /// Initializes a new indicator bound to a symbol, so a multi-symbol report draws it only on that
        /// symbol's chart.
        /// </summary>
        public Indicator(string name, string symbol, IndicatorPane pane, IReadOnlyList<IndicatorSeries> series)
        {
            Name = name;
            Symbol = symbol;
            Pane = pane;
            Series = series;
        }

        /// <summary>Gets the display name of the indicator (e.g. <c>"MACD"</c> or <c>"SMA(20)"</c>).</summary>
        public string Name { get; }

        /// <summary>Gets the symbol this indicator belongs to, or <c>null</c> if it applies to every symbol.</summary>
        public string Symbol { get; }

        /// <summary>Gets the pane all of this indicator's series are drawn in.</summary>
        public IndicatorPane Pane { get; }

        /// <summary>Gets the ordered series making up the indicator, all sharing its pane.</summary>
        public IReadOnlyList<IndicatorSeries> Series { get; }
    }
}
