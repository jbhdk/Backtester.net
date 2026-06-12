using System;

namespace Backtester.Core
{
    public class Trade
    {
        public string Id { get; set; }
        public string OrderId { get; set; }
        public string PositionId { get; set; }
        public string Symbol { get; set; }
        public OrderSide Side { get; set; }
        public decimal Price { get; set; }
        public int Quantity { get; set; }
        public decimal Commission { get; set; }
        public decimal Slippage { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
