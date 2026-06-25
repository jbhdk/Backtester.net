namespace Backtester.Core
{
    /// <summary>
    /// The bracket role of the order that produced a fill: a neutral fact about a <see cref="Trade"/>
    /// recorded by the broker when it arms a bracket's protective legs. Entry and plain strategy fills
    /// carry <see cref="None"/>; the round trip's exit reason is derived from this on its exit trade.
    /// </summary>
    public enum BracketLeg
    {
        /// <summary>The fill did not come from a bracket's protective leg (an entry or a plain order).</summary>
        None = 0,

        /// <summary>The fill came from a bracket's stop leg, including a trailed stop.</summary>
        StopLoss = 1,

        /// <summary>The fill came from a bracket's take-profit (target limit) leg.</summary>
        TakeProfit = 2
    }
}
