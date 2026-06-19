namespace Backtester.Core
{
    /// <summary>
    /// Designates where an indicator series should be drawn in the report: overlaid on the price
    /// chart, or in its own separate pane below it.
    /// </summary>
    public enum IndicatorPane
    {
        /// <summary>Drawn on the price chart, sharing its vertical scale (e.g. a moving average).</summary>
        PriceOverlay,

        /// <summary>Drawn in a separate pane with its own scale (e.g. RSI or ATR).</summary>
        SeparatePane
    }
}
