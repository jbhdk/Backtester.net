using System;
using System.Collections.Generic;

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

        /// <summary>Gets or sets the current net quantity (positive = long, negative = short, zero = flat).</summary>
        public int Quantity { get; set; }

        /// <summary>Gets or sets the volume-weighted average entry price.</summary>
        public decimal AveragePrice { get; set; }

        /// <summary>Gets the list of all trades that have affected this position.</summary>
        public List<Trade> Trades { get; } = new();

        /// <summary>Gets or sets the strategy-supplied metadata attached to this position.</summary>
        public PositionMetadata Metadata { get; set; }

        /// <summary>
        /// Applies a trade to this position, updating signed quantity and average price. A fill in the
        /// position's own direction (or from flat) grows the magnitude and recomputes the volume-weighted
        /// average entry price; an opposing fill reduces the magnitude toward zero and leaves the average
        /// unchanged. Callers must not pass a fill that would flip the sign — overshoot is clamped upstream.
        /// </summary>
        public void AddTrade(Trade trade)
        {
            Trades.Add(trade);
            int delta = trade.Side == OrderSide.Buy ? trade.Quantity : -trade.Quantity;
            if (Quantity == 0 || Math.Sign(delta) == Math.Sign(Quantity))
            {
                decimal totalCost = AveragePrice * Math.Abs(Quantity) + trade.Price * trade.Quantity;
                Quantity += delta;
                AveragePrice = totalCost / Math.Abs(Quantity);
            }
            else
            {
                Quantity += delta;
            }
        }
    }
}
