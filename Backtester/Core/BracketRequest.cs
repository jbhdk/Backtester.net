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

        /// <summary>
        /// Gets or sets the stop-loss as a fill-relative offset: a positive per-share distance the engine
        /// subtracts from (long) or adds to (short) the actual fill at fill time to place the stop on the
        /// protective side. Must be greater than zero. Mutually exclusive with <see cref="StopPrice"/> —
        /// setting both for the stop leg is caller misuse.
        /// </summary>
        public decimal? StopOffset { get; set; }

        /// <summary>
        /// Gets or sets the take-profit as a fill-relative offset: a positive per-share distance the engine
        /// adds to (long) or subtracts from (short) the actual fill at fill time. Must be greater than zero.
        /// Mutually exclusive with <see cref="TargetPrice"/> — setting both for the target leg is caller misuse.
        /// </summary>
        public decimal? TargetOffset { get; set; }
    }
}
