namespace Backtester.Core
{
    /// <summary>
    /// An entry order with one or two attached protective legs for bracket order submission: a
    /// stop-loss and/or a take-profit. At least one leg must be set (a request with neither is a plain
    /// order, not a bracket). When both are set they form an OCO group; a single leg simply rests until
    /// it fills or the position is closed by a signal exit.
    /// </summary>
    public class BracketRequest
    {
        /// <summary>Gets or sets the entry order details.</summary>
        public OrderRequest Entry { get; set; }

        /// <summary>Gets or sets the stop-loss price for the protective stop leg, or null to attach no stop.</summary>
        public decimal? StopPrice { get; set; }

        /// <summary>Gets or sets the take-profit price for the protective limit leg, or null to attach no target.</summary>
        public decimal? TargetPrice { get; set; }
    }
}
