namespace Backtester.Core
{
    /// <summary>
    /// Where a bracket stands in its lifecycle, as reported by its <see cref="BracketHandle"/>. A bracket
    /// moves forward only: it is created pending, arms once its entry fills, and retires once the position
    /// its legs protected is resolved — by a leg filling, by the sibling of a filled leg being taken out with
    /// it, or by a signal exit cancelling whatever still rested.
    ///
    /// This is the question a strategy actually has of a bracket. Reading it off an order ID instead — "the
    /// stop order ID is non-null, so the entry must have filled" — misreads a target-only bracket, which arms
    /// no stop leg and so carries a null stop order ID for the whole life of its position.
    /// </summary>
    public enum BracketState
    {
        /// <summary>The entry order is still working and no protective leg exists yet.</summary>
        Pending,

        /// <summary>The entry filled and at least one protective leg rests against the open position.</summary>
        Armed,

        /// <summary>The position is resolved and no leg rests: the bracket has nothing left to answer for.</summary>
        Retired
    }
}
