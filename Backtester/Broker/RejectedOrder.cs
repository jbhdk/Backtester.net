using System;
using Backtester.Core;

namespace Backtester.Broker
{
    /// <summary>
    /// Records a single order the broker declined, capturing what was attempted and why so the run can
    /// surface rejected orders for audit. The Reg-T margin gate is the current source of rejections.
    /// </summary>
    public class RejectedOrder
    {
        /// <summary>Gets or sets the ticker symbol the rejected order targeted.</summary>
        public string Symbol { get; set; }

        /// <summary>Gets or sets the side (Buy/Sell) of the rejected order.</summary>
        public OrderSide Side { get; set; }

        /// <summary>Gets or sets the (already-sized) quantity the rejected order requested.</summary>
        public int Quantity { get; set; }

        /// <summary>Gets or sets the price the order was valued at when it was rejected.</summary>
        public decimal Price { get; set; }

        /// <summary>Gets or sets the bar timestamp at which the order was attempted.</summary>
        public DateTime Timestamp { get; set; }

        /// <summary>Gets or sets the reason the order was rejected (e.g. <c>"Not enough funds"</c>).</summary>
        public string Reason { get; set; }
    }
}
