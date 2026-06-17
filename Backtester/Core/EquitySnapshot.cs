using System;

namespace Backtester.Core
{
    /// <summary>
    /// A point-in-time snapshot of portfolio equity recorded after each bar.
    /// </summary>
    public class EquitySnapshot
    {
        /// <summary>Gets or sets the UTC timestamp of this snapshot.</summary>
        public DateTime Timestamp { get; set; }

        /// <summary>Gets or sets the available cash at this point in time.</summary>
        public decimal Cash { get; set; }

        /// <summary>Gets or sets the total unrealized profit/loss on open positions.</summary>
        public decimal UnrealizedPnL { get; set; }

        /// <summary>Gets or sets the cumulative realized profit/loss from closed trades.</summary>
        public decimal RealizedPnL { get; set; }

        /// <summary>Gets or sets the total portfolio equity (cash plus unrealized value).</summary>
        public decimal TotalEquity { get; set; }
    }
}
