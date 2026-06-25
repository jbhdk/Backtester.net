namespace Backtester.Core
{
    /// <summary>
    /// The geometric shape a single indicator series is drawn as within its pane. Structural only —
    /// colour, line width, and fill are rendering concerns the report derives, not stored here.
    /// </summary>
    public enum IndicatorShape
    {
        /// <summary>A simple line tracing the series values (e.g. a moving average or a MACD line).</summary>
        Line,

        /// <summary>A line with the area beneath it filled (e.g. RSI or ATR in its own pane).</summary>
        Area,

        /// <summary>Vertical bars from a baseline (e.g. a MACD histogram).</summary>
        Histogram
    }
}
