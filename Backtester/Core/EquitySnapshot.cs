using System;
using System.Collections.Generic;

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

        /// <summary>Gets or sets the mark-to-market portfolio equity (cash plus current market value of open positions).</summary>
        public decimal MarkedEquity { get; set; }

        // Key: symbol/ticker -> that symbol's isolated equity at this snapshot (starting capital plus the
        // symbol's own realized and unrealized P&L, as if it alone traded the whole account).
        /// <summary>Gets or sets each traded symbol's isolated equity at this snapshot.</summary>
        public IReadOnlyDictionary<string, decimal> EquityBySymbol { get; set; }

        // Key: symbol/ticker -> the signed market value of that symbol's open position at this snapshot
        // (latest close × signed quantity; negative for a short). Only symbols with an open position
        // appear, so the map is empty when the account is flat. Underpins the market-exposure and
        // capital-invested metrics; null on snapshots recorded before this field existed.
        /// <summary>Gets or sets the market value of each open position at this snapshot, keyed by symbol.</summary>
        public IReadOnlyDictionary<string, decimal> PositionValueBySymbol { get; set; }
    }
}
