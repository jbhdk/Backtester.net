using Backtester.Core;

namespace Backtester.Strategies
{
    /// <summary>
    /// Optional seam a strategy implements to observe its round trips live, the moment each one closes
    /// during a run. The engine delivers a closed round trip after that bar's fills and equity snapshot
    /// and before the bar's <c>OnBar</c>, so the strategy can react on the same bar (e.g. pause entries
    /// after a run of losses). Observation is opt-in: a strategy that does not implement this receives
    /// nothing. The callback informs only — what the strategy does with the result is its own decision;
    /// the engine carries on regardless.
    /// </summary>
    public interface IRoundTripObserver
    {
        /// <summary>
        /// Called once for each round trip as it closes, in the order the round trips closed. A partial
        /// exit and a multi-symbol bar can each close more than one round trip on a bar; each arrives as
        /// its own call carrying that round trip's realized PnL, direction, exit reason, bars held, and
        /// entry/exit prices and times.
        /// </summary>
        void OnRoundTripClosed(RoundTrip roundTrip);
    }
}
