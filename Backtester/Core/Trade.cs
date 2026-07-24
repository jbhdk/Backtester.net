using System;

namespace Backtester.Core
{
    /// <summary>
    /// Represents an executed fill resulting from an order being matched.
    /// </summary>
    public class Trade
    {
        /// <summary>Gets or sets the unique identifier for this trade.</summary>
        public string Id { get; set; }

        /// <summary>Gets or sets the identifier of the order that generated this trade.</summary>
        public string OrderId { get; set; }

        /// <summary>Gets or sets the identifier of the position this trade belongs to.</summary>
        public string PositionId { get; set; }

        /// <summary>Gets or sets the ticker symbol that was traded.</summary>
        public string Symbol { get; set; }

        /// <summary>Gets or sets whether this trade was a buy or sell.</summary>
        public OrderSide Side { get; set; }

        /// <summary>Gets or sets the fill price after slippage.</summary>
        public decimal Price { get; set; }

        /// <summary>Gets or sets the number of shares or contracts filled.</summary>
        public int Quantity { get; set; }

        /// <summary>Gets or sets the commission charged for this trade.</summary>
        public decimal Commission { get; set; }

        /// <summary>Gets or sets the absolute slippage cost incurred on this trade.</summary>
        public decimal Slippage { get; set; }

        /// <summary>Gets or sets the UTC timestamp when this trade was executed.</summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// Gets or sets the bracket role of the order that produced this fill. Entry and plain strategy
        /// fills are <see cref="BracketLeg.None"/>; the broker stamps a protective leg's role here.
        /// </summary>
        public BracketLeg Leg { get; set; }

        /// <summary>
        /// Gets or sets the protective stop-loss price declared for the entry that produced this fill,
        /// stamped by the broker on an entry fill so the round trip can carry its initial risk. Null when
        /// the entry declared no stop; unset (null) on protective-leg and plain reducing fills.
        /// </summary>
        public decimal? EntryStopPrice { get; set; }
    }
}
