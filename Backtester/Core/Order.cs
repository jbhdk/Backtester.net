using System;

namespace Backtester.Core
{
    /// <summary>
    /// Represents an order submitted to the broker simulator.
    /// </summary>
    public class Order
    {
        /// <summary>Gets or sets the unique identifier for this order.</summary>
        public string Id { get; set; }

        /// <summary>Gets or sets the ticker symbol this order targets.</summary>
        public string Symbol { get; set; }

        /// <summary>Gets or sets whether this is a buy or sell order.</summary>
        public OrderSide Side { get; set; }

        /// <summary>Gets or sets the execution style (market, limit, stop).</summary>
        public OrderType Type { get; set; }

        /// <summary>Gets or sets the limit or stop price, if applicable.</summary>
        public decimal? Price { get; set; }

        /// <summary>Gets or sets the number of shares or contracts to trade.</summary>
        public int Quantity { get; set; }

        /// <summary>Gets or sets the UTC timestamp when this order was submitted.</summary>
        public DateTime SubmittedAt { get; set; }
    }
}
