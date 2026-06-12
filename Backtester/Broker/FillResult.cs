using System;

namespace Backtester.Broker
{
    public class FillResult
    {
        public string OrderId { get; set; }
        public string TradeId { get; set; }
        public decimal Price { get; set; }
        public int Quantity { get; set; }
    }
}
