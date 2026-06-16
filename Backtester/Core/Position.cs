using System;
using System.Collections.Generic;

namespace Backtester.Core
{
    public class Position
    {
        public string Id { get; set; }
        public string Symbol { get; set; }
        public int Quantity { get; set; }
        public decimal AveragePrice { get; set; }
        public List<Trade> Trades { get; } = new();
        public PositionMetadata Metadata { get; set; }

        public void AddTrade(Trade trade)
        {
            Trades.Add(trade);
            if (trade.Side == OrderSide.Buy)
            {
                var totalCost = AveragePrice * Quantity + trade.Price * trade.Quantity;
                Quantity += trade.Quantity;
                AveragePrice = totalCost / Quantity;
            }
            else
            {
                Quantity -= trade.Quantity;
            }
        }

        public void UpdateWithBar(Candle bar)
        {
            throw new System.NotImplementedException();
        }
    }
}
