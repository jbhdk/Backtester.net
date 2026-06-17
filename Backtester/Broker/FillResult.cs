using System;

namespace Backtester.Broker
{
    /// <summary>
    /// Describes a single fill produced by the fill model for a pending order.
    /// </summary>
    public class FillResult
    {
        /// <summary>Gets or sets the identifier of the order that was filled.</summary>
        public string OrderId { get; set; }

        /// <summary>Gets or sets the unique identifier assigned to the resulting trade.</summary>
        public string TradeId { get; set; }

        /// <summary>Gets or sets the raw fill price before slippage is applied.</summary>
        public decimal Price { get; set; }

        /// <summary>Gets or sets the number of shares or contracts filled.</summary>
        public int Quantity { get; set; }
    }
}
