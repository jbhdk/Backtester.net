using System;

namespace Backtester.Core
{
    public class Order
    {
        public string Id { get; set; }
        public string Symbol { get; set; }
        public OrderSide Side { get; set; }
        public OrderType Type { get; set; }
        public decimal? Price { get; set; }
        public int Quantity { get; set; }
        public DateTime SubmittedAt { get; set; }
    }
}
