using System.Collections.Generic;

namespace Backtester.Core
{
    /// <summary>
    /// A single named, time-aligned line within an <see cref="Indicator"/>, drawn with a given
    /// <see cref="IndicatorShape"/>. Placement (pane) and symbol binding live on the owning Indicator,
    /// not here; this carries no style or colour metadata — that is a rendering concern.
    /// </summary>
    public class IndicatorSeries
    {
        /// <summary>
        /// Initializes a new series with the shape it should be drawn as.
        /// </summary>
        public IndicatorSeries(string name, IndicatorShape shape, IReadOnlyList<IndicatorPoint> points)
        {
            Name = name;
            Shape = shape;
            Points = points;
        }

        /// <summary>Gets the display name of the series (e.g. <c>"SMA(20)"</c>).</summary>
        public string Name { get; }

        /// <summary>Gets the shape the series is drawn as (line, area, or histogram).</summary>
        public IndicatorShape Shape { get; }

        /// <summary>Gets the time-aligned points making up the series.</summary>
        public IReadOnlyList<IndicatorPoint> Points { get; }
    }
}
