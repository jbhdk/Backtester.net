namespace Backtester.Core
{
    /// <summary>
    /// The strategy's view onto a submitted bracket: where it stands in its lifecycle, and the order IDs of
    /// the orders it holds. The bracket owns both — the handle is how it answers from outside the broker, so
    /// its <see cref="State"/> is the bracket's own state rather than a copy that could drift from it.
    ///
    /// Ask <see cref="State"/> the lifecycle question directly; do not infer it from an order ID being
    /// populated. A target-only bracket arms no stop leg, so its <see cref="StopOrderId"/> stays null for the
    /// whole life of its position and "the stop order ID is set" would read as an entry that never filled.
    /// </summary>
    public class BracketHandle
    {
        /// <summary>
        /// Gets where the bracket stands: <see cref="BracketState.Pending"/> while its entry works,
        /// <see cref="BracketState.Armed"/> once the entry filled and its legs rest, and
        /// <see cref="BracketState.Retired"/> once nothing rests and the position is resolved.
        /// </summary>
        public BracketState State { get; internal set; }

        /// <summary>Gets or sets the entry order ID assigned at submission time.</summary>
        public string EntryOrderId { get; set; }

        /// <summary>Gets or sets the stop-loss order ID; non-null once a requested stop leg is armed.</summary>
        public string StopOrderId { get; set; }

        /// <summary>Gets or sets the take-profit order ID; non-null once a requested target leg is armed.</summary>
        public string TargetOrderId { get; set; }
    }
}
