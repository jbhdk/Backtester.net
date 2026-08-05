using Backtester.Core;

namespace Backtester.Broker
{
    /// <summary>
    /// What one fill meant to the <see cref="Bracket"/> that owned the filled order: whether it was the
    /// entry — which is what leaves the bracket ready to arm — which protective leg it was, and which
    /// sibling leg the fill takes with it under one-cancels-the-other.
    ///
    /// A single-leg bracket answers with no sibling rather than with a different kind of answer, so the
    /// broker executes one path whether the bracket armed one leg or two (ADR 0002).
    /// </summary>
    internal sealed record BracketFillOutcome
    {
        /// <summary>The answer for a fill that belonged to no live bracket: no entry, no leg, nothing to cancel.</summary>
        internal static readonly BracketFillOutcome None = new();

        /// <summary>Gets whether the filled order was the bracket's entry, which is what makes it ready to arm.</summary>
        internal bool IsEntry { get; init; }

        /// <summary>
        /// Gets the role of the filled leg, stamped onto the resulting trade (the round trip's exit reason
        /// is derived from it). None when the fill was an entry or belonged to no bracket.
        /// </summary>
        internal BracketLeg Leg { get; init; }

        /// <summary>
        /// Gets the sibling leg the broker must cancel, or null when the bracket had no other resting leg —
        /// a single-leg bracket, or one whose sibling was already cancelled.
        /// </summary>
        internal string SiblingOrderId { get; init; }
    }
}
