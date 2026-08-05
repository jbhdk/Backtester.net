namespace Backtester.Core
{
    /// <summary>
    /// Carries the intent to trade from a strategy to the broker simulator.
    /// </summary>
    public class OrderRequest
    {
        /// <summary>Gets or sets the ticker symbol to trade.</summary>
        public string Symbol { get; set; }

        /// <summary>Gets or sets whether to buy or sell.</summary>
        public OrderSide Side { get; set; }

        /// <summary>Gets or sets the execution style (market, limit, stop).</summary>
        public OrderType Type { get; set; }

        /// <summary>Gets or sets the limit or stop price, if applicable.</summary>
        public decimal? Price { get; set; }

        /// <summary>Gets or sets the requested number of shares or contracts.</summary>
        public int Quantity { get; set; }

        /// <summary>Gets or sets the intended stop-loss price for risk-per-trade sizing.</summary>
        public decimal? StopPrice { get; set; }

        /// <summary>
        /// Gets or sets the intended per-share stop distance for risk-per-trade sizing when the protective
        /// stop is fill-relative and its absolute price is not yet known at submit time (a bracket entry
        /// whose stop is a <see cref="BracketRequest.StopOffset"/>). A positive distance; the broker copies
        /// the bracket's stop offset here before sizing. Preferred over <see cref="StopPrice"/> for sizing,
        /// since a fill-relative stop has no absolute anchor to measure against yet.
        /// </summary>
        public decimal? StopOffset { get; set; }

        /// <summary>Gets or sets the priority for order processing (higher = sooner).</summary>
        public int Priority { get; set; }

        /// <summary>Gets or sets arbitrary strategy-supplied metadata for this order.</summary>
        public object ClientMetadata { get; set; }

        /// <summary>
        /// Returns a shallow copy of this request, letting the broker apply its own decisions (the sized
        /// quantity, a bracket's sizing offset) without writing into an object the caller still holds and
        /// may reuse on a later bar. Every property carries over, including any added later.
        /// <see cref="ClientMetadata"/> is shared by reference: the broker never clones a caller's object graph.
        /// </summary>
        public OrderRequest Copy()
        {
            return (OrderRequest)MemberwiseClone();
        }
    }
}
