namespace Backtester.Core
{
    /// <summary>
    /// The direction of a position or round trip.
    /// </summary>
    public enum PositionDirection
    {
        /// <summary>A long position: entered by buying, exited by selling.</summary>
        Long,

        /// <summary>A short position: entered by selling, exited (covered) by buying.</summary>
        Short
    }
}
