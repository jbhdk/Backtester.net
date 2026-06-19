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
        /// Initializes a new series with the given name, pane designation, and time-aligned points.
        /// </summary>
        public IndicatorSeries(string name, IndicatorPane pane, IReadOnlyList<IndicatorPoint> points)
        {
            Name = name;
            Pane = pane;
            Points = points;
        }

        /// <summary>Gets the display name of the series (e.g. <c>"SMA(20)"</c>).</summary>
        public string Name { get; }

        /// <summary>Gets the pane the series should be drawn in.</summary>
        public IndicatorPane Pane { get; }

        /// <summary>Gets the time-aligned points making up the series.</summary>
        public IReadOnlyList<IndicatorPoint> Points { get; }
    }
}
