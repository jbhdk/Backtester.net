using System;

namespace Backtester.Core
{
    /// <summary>
    /// A complete entry-to-exit cycle for a position: one or more buys paired with a closing sell.
    /// </summary>
    public class RoundTrip
    {
        /// <summary>Gets or sets the ticker symbol traded in this round trip.</summary>
        public string Symbol { get; set; }

        /// <summary>Gets or sets the direction of this round trip (long: buy then sell; short: sell then buy).</summary>
        public PositionDirection Direction { get; set; }

        /// <summary>Gets or sets the volume-weighted average entry price.</summary>
        public decimal EntryPrice { get; set; }

        /// <summary>Gets or sets the exit fill price.</summary>
        public decimal ExitPrice { get; set; }

        /// <summary>Gets or sets the number of shares exited.</summary>
        public int Quantity { get; set; }

        /// <summary>Gets or sets the realized profit/loss for this round trip, excluding commission and slippage.</summary>
        public decimal RealizedPnL { get; set; }

        /// <summary>
        /// Gets or sets the currency this round trip stood to lose if its entry stop had been hit, before
        /// any trailing: the per-share stop distance frozen at entry times this trip's quantity. Null when
        /// the entry declared no protective stop, in which case no R-multiple is defined.
        /// </summary>
        public decimal? InitialRisk { get; set; }

        /// <summary>Gets or sets the number of bars the position was held before exit.</summary>
        public int BarsHeld { get; set; }

        /// <summary>Gets or sets the UTC timestamp of the entry trade that opened this round trip.</summary>
        public DateTime EntryTime { get; set; }

        /// <summary>Gets or sets the UTC timestamp of the exit trade that closed this round trip.</summary>
        public DateTime ExitTime { get; set; }

        /// <summary>Gets or sets why this round trip closed, derived from the bracket leg of its exit trade.</summary>
        public ExitReason ExitReason { get; set; }
    }
}
