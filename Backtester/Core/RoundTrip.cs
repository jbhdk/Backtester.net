namespace Backtester.Core
{
    /// <summary>
    /// A complete entry-to-exit cycle for a position: one or more buys paired with a closing sell.
    /// </summary>
    public class RoundTrip
    {
        /// <summary>Gets or sets the ticker symbol traded in this round trip.</summary>
        public string Symbol { get; set; }

        /// <summary>Gets or sets the volume-weighted average entry price.</summary>
        public decimal EntryPrice { get; set; }

        /// <summary>Gets or sets the exit fill price.</summary>
        public decimal ExitPrice { get; set; }

        /// <summary>Gets or sets the number of shares exited.</summary>
        public int Quantity { get; set; }

        /// <summary>Gets or sets the realized profit/loss for this round trip, excluding commission and slippage.</summary>
        public decimal RealizedPnL { get; set; }

        /// <summary>Gets or sets the number of bars the position was held before exit.</summary>
        public int BarsHeld { get; set; }
    }
}
