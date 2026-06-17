using System;
using System.Collections.Generic;
using Backtester.Core;

namespace Backtester.Core
{
    /// <summary>
    /// Tracks an open position in a single symbol, including all contributing trades.
    /// </summary>
    public class Position
    {
        /// <summary>Gets or sets the unique identifier for this position.</summary>
        public string Id { get; set; }

        /// <summary>Gets or sets the ticker symbol held in this position.</summary>
        public string Symbol { get; set; }

        /// <summary>Gets or sets the current net quantity (positive = long, zero = flat).</summary>
        public int Quantity { get; set; }

        /// <summary>Gets or sets the volume-weighted average entry price.</summary>
        public decimal AveragePrice { get; set; }

        /// <summary>Gets the list of all trades that have affected this position.</summary>
        public List<Trade> Trades { get; } = new();

        /// <summary>Gets or sets the strategy-supplied metadata attached to this position.</summary>
        public PositionMetadata Metadata { get; set; }

        /// <summary>
        /// Applies a trade to this position, updating quantity and average price.
        /// </summary>
        public void AddTrade(Trade trade)
        {
            Trades.Add(trade);
            if (trade.Side == OrderSide.Buy)
            {
                decimal totalCost = AveragePrice * Quantity + trade.Price * trade.Quantity;
                Quantity += trade.Quantity;
                AveragePrice = totalCost / Quantity;
            }
            else
            {
                Quantity -= trade.Quantity;
            }
        }

        /// <summary>
        /// Updates unrealized P&amp;L and any bar-level position state using the latest candle.
        /// </summary>
        public void UpdateWithBar(Candle bar)
        {
            throw new NotImplementedException();
        }
    }
}
