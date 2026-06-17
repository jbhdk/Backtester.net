using System;

namespace Backtester.Core
{
    /// <summary>
    /// Carries the intent to trade from a strategy to the broker simulator.
    /// </summary>
    public class OrderRequest
    {
        /// <summary>Gets or sets the ticker symbol to trade.</summary>
        public string Symbol { get; set; }

        /// <summary>Gets or sets whether to buy or sell.</summary>
        public OrderSide Side { get; set; }

        /// <summary>Gets or sets the execution style (market, limit, stop).</summary>
        public OrderType Type { get; set; }

        /// <summary>Gets or sets the limit or stop price, if applicable.</summary>
        public decimal? Price { get; set; }

        /// <summary>Gets or sets the requested number of shares or contracts.</summary>
        public int Quantity { get; set; }

        /// <summary>Gets or sets the priority for order processing (higher = sooner).</summary>
        public int Priority { get; set; }

        /// <summary>Gets or sets arbitrary strategy-supplied metadata for this order.</summary>
        public object ClientMetadata { get; set; }
    }
}
