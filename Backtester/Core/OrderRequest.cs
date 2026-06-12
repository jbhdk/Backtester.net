using System;

namespace Backtester.Core
{
    public class OrderRequest
    {
        public string Symbol { get; set; }
        public OrderSide Side { get; set; }
        public OrderType Type { get; set; }
        public decimal? Price { get; set; }
        public int Quantity { get; set; }
        public int Priority { get; set; }
        public object ClientMetadata { get; set; }
    }
}
