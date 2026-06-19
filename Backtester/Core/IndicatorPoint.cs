using System;

namespace Backtester.Core
{
    /// <summary>
    /// A single time-aligned sample of an indicator series: a value at a bar timestamp.
    /// </summary>
    public class IndicatorPoint
    {
        /// <summary>Gets or sets the UTC timestamp of the bar this value aligns to.</summary>
        public DateTime Timestamp { get; set; }

        /// <summary>Gets or sets the indicator value at that timestamp.</summary>
        public decimal Value { get; set; }
    }
}
