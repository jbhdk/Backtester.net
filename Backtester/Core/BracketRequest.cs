namespace Backtester.Core
{
    /// <summary>
    /// An entry order with attached stop-loss and take-profit prices for bracket order submission.
    /// </summary>
    public class BracketRequest
    {
        /// <summary>Gets or sets the entry order details.</summary>
        public OrderRequest Entry { get; set; }

        /// <summary>Gets or sets the explicit stop-loss price for the protective sell stop.</summary>
        public decimal StopPrice { get; set; }

        /// <summary>Gets or sets the explicit take-profit price for the sell limit order.</summary>
        public decimal TargetPrice { get; set; }
    }
}
