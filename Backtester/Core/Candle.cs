using System;

namespace Backtester.Core
{
    /// <summary>
    /// Represents a single OHLCV price bar for a financial instrument.
    /// </summary>
    public class Candle
    {
        /// <summary>Gets or sets the UTC timestamp of the bar.</summary>
        public DateTime Timestamp { get; set; }

        /// <summary>Gets or sets the opening price.</summary>
        public decimal Open { get; set; }

        /// <summary>Gets or sets the highest price during the bar.</summary>
        public decimal High { get; set; }

        /// <summary>Gets or sets the lowest price during the bar.</summary>
        public decimal Low { get; set; }

        /// <summary>Gets or sets the closing price.</summary>
        public decimal Close { get; set; }

        /// <summary>Gets or sets the traded volume during the bar.</summary>
        public decimal Volume { get; set; }
    }
}
