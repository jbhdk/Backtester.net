using System;

namespace Backtester.Core
{
    /// <summary>
    /// An entry order with one or two attached protective legs for bracket order submission: a
    /// stop-loss and/or a take-profit. At least one leg must be given (a request with neither is a plain
    /// order, not a bracket), which is what construction enforces. When both are given they form an OCO
    /// group; a single leg simply rests until it fills or the position is closed by a signal exit.
    /// </summary>
    public class BracketRequest
    {
        /// <summary>
        /// Creates the request for a bracketed entry, rejecting one that cannot form a legal bracket: a
        /// request with no leg at all, an unprotected entry being a plain order rather than a bracket
        /// (ADR 0002). Each leg is given as a <see cref="BracketLegSpec"/>, which is where the choice
        /// between an absolute and a fill-relative form is made, or left null to attach no such leg.
        /// </summary>
        public BracketRequest(OrderRequest entry, BracketLegSpec stopLeg = null, BracketLegSpec targetLeg = null)
        {
            if (entry == null)
            {
                throw new ArgumentNullException(nameof(entry), "A bracket must have an entry order.");
            }
            if (stopLeg == null && targetLeg == null)
            {
                throw new ArgumentException("A bracket must have at least one leg (a stop-loss and/or a take-profit).");
            }

            Entry = entry;
            StopLeg = stopLeg;
            TargetLeg = targetLeg;
        }

        /// <summary>Gets the entry order details.</summary>
        public OrderRequest Entry { get; }

        /// <summary>Gets the protective stop leg, or null when this bracket attaches no stop.</summary>
        public BracketLegSpec StopLeg { get; }

        /// <summary>Gets the take-profit leg, or null when this bracket attaches no target.</summary>
        public BracketLegSpec TargetLeg { get; }
    }
}
