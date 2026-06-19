namespace Backtester.Report
{
    /// <summary>
    /// A single price bar shaped for the chart library: the timestamp encoded as UTC seconds (as
    /// Lightweight Charts requires for intraday data) and the open/high/low/close prices.
    /// </summary>
    public class ChartCandle
    {
        /// <summary>Gets or sets the bar's timestamp encoded as UTC seconds since the Unix epoch.</summary>
        public long Time { get; set; }

        /// <summary>Gets or sets the opening price.</summary>
        public decimal Open { get; set; }

        /// <summary>Gets or sets the highest price during the bar.</summary>
        public decimal High { get; set; }

        /// <summary>Gets or sets the lowest price during the bar.</summary>
        public decimal Low { get; set; }

        /// <summary>Gets or sets the closing price.</summary>
        public decimal Close { get; set; }
    }
}
