using System.Collections.Generic;

namespace Backtester.Core
{
    /// <summary>
    /// A named, time-aligned series a strategy exposed for visualization, together with the pane it
    /// should be drawn in. Carries no style or colour metadata — that is a rendering concern.
    /// </summary>
    public class IndicatorSeries
    {
        /// <summary>
        /// Initializes a new series not bound to any symbol (drawn on every symbol's chart). Suitable
        /// for a single-symbol run where the symbol is unambiguous.
        /// </summary>
        public IndicatorSeries(string name, IndicatorPane pane, IReadOnlyList<IndicatorPoint> points)
            : this(name, null, pane, points)
        {
        }

        /// <summary>
        /// Initializes a new series bound to a symbol, so a multi-symbol report draws it only on that
        /// symbol's chart.
        /// </summary>
        public IndicatorSeries(string name, string symbol, IndicatorPane pane, IReadOnlyList<IndicatorPoint> points)
        {
            Name = name;
            Symbol = symbol;
            Pane = pane;
            Points = points;
        }

        /// <summary>Gets the display name of the series (e.g. <c>"SMA(20)"</c>).</summary>
        public string Name { get; }

        /// <summary>Gets the symbol this series belongs to, or <c>null</c> if it applies to every symbol.</summary>
        public string Symbol { get; }

        /// <summary>Gets the pane the series should be drawn in.</summary>
        public IndicatorPane Pane { get; }

        /// <summary>Gets the time-aligned points making up the series.</summary>
        public IReadOnlyList<IndicatorPoint> Points { get; }
    }
}
