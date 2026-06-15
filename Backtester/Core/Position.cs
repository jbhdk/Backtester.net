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
            throw new System.NotImplementedException();
        }

        public void UpdateWithBar(Candle bar)
        {
            throw new System.NotImplementedException();
        }
    }
}
