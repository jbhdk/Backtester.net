namespace Backtester.Report
{
    /// <summary>
    /// A single point of an indicator line series shaped for the chart library: the timestamp encoded
    /// as UTC seconds (so it aligns to the candle time axis) and the indicator value at that bar.
    /// </summary>
    public class ChartLinePoint
    {
        /// <summary>Gets or sets the bar's timestamp encoded as UTC seconds since the Unix epoch.</summary>
        public long Time { get; set; }

        /// <summary>Gets or sets the indicator value at that timestamp.</summary>
        public decimal Value { get; set; }
    }
}
