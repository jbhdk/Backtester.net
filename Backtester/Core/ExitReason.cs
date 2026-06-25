namespace Backtester.Core
{
    /// <summary>
    /// Why a <see cref="RoundTrip"/> closed, derived from the bracket leg of its exit trade.
    /// </summary>
    public enum ExitReason
    {
        /// <summary>Closed by a non-bracket order the strategy submitted (a deliberate exit or a reversal flatten).</summary>
        Signal = 0,

        /// <summary>Closed when the bracket's take-profit (target limit) leg filled.</summary>
        TakeProfit = 1,

        /// <summary>Closed when the bracket's stop leg filled, including a trailed stop.</summary>
        StopLoss = 2
    }
}
