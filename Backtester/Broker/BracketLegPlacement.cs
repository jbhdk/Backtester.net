using Backtester.Core;

namespace Backtester.Broker
{
    /// <summary>
    /// One protective leg a <see cref="Bracket"/> decided to place when its entry filled: everything the
    /// broker needs to materialize a working order, and nothing about the order book. The bracket mints the
    /// order ID itself, so the <see cref="BracketHandle"/> is complete the moment the entry fills — before
    /// the broker books anything.
    /// </summary>
    internal sealed record BracketLegPlacement
    {
        /// <summary>Gets the order ID the bracket minted for this leg.</summary>
        internal string OrderId { get; init; }

        /// <summary>Gets the side that closes the entry: Sell for a long entry, Buy for a short.</summary>
        internal OrderSide Side { get; init; }

        /// <summary>Gets the execution style: Stop for the stop-loss leg, Limit for the take-profit leg.</summary>
        internal OrderType Type { get; init; }

        /// <summary>Gets the absolute trigger price, already resolved against the actual entry fill.</summary>
        internal decimal Price { get; init; }

        /// <summary>Gets the number of shares or contracts the leg covers — the entry's filled size.</summary>
        internal int Quantity { get; init; }

        /// <summary>Gets which leg of the bracket this is, stamped onto the fill it eventually produces.</summary>
        internal BracketLeg Leg { get; init; }
    }
}
