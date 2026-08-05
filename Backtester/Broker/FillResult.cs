namespace Backtester.Broker
{
    /// <summary>
    /// Describes a single fill produced by the fill model for a pending order.
    /// </summary>
    /// <remarks>
    /// Public by reachability, not by allowlist (ADR 0034): it is the element type returned by
    /// <see cref="IFillModel.DetermineFills"/>, so a strategy author supplying their own Fill
    /// Execution model has to name it. Narrowing it would drag <see cref="IFillModel"/> internal too.
    /// </remarks>
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
