namespace Backtester.Broker
{
    /// <summary>
    /// Where a <see cref="Bracket"/> stands in its lifecycle. A bracket moves forward only: it is created
    /// pending, arms once its entry fills, and retires once the position its legs protected is resolved —
    /// by a leg filling, by the sibling of a filled leg being taken out with it, or by a signal exit
    /// cancelling whatever still rested.
    /// </summary>
    internal enum BracketState
    {
        /// <summary>The entry order is still working and no protective leg exists yet.</summary>
        Pending,

        /// <summary>The entry filled and at least one protective leg rests against the open position.</summary>
        Armed,

        /// <summary>The position is resolved and no leg rests: the bracket has nothing left to answer for.</summary>
        Retired
    }
}
