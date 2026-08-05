using System;

namespace Backtester.Core
{
    /// <summary>
    /// The entry-time specification of one of a bracket's protective legs — its stop-loss or its
    /// take-profit — given either as an absolute price or as a fill-relative offset. A leg spec is built
    /// through one of the two factories, so a leg carrying both forms cannot be constructed.
    /// </summary>
    public sealed class BracketLegSpec
    {
        private readonly decimal? _price;
        private readonly decimal? _offset;

        private BracketLegSpec(decimal? price, decimal? offset)
        {
            _price = price;
            _offset = offset;
        }

        /// <summary>
        /// Specifies the leg as an absolute price: it rests at exactly this level whatever the entry fills at.
        /// </summary>
        public static BracketLegSpec AtPrice(decimal price)
        {
            return new BracketLegSpec(price, null);
        }

        /// <summary>
        /// Specifies the leg as a fill-relative offset: a per-share distance the engine applies to the actual
        /// entry fill, on the leg's protective side, once the entry fills (ADR 0025). The distance is the
        /// magnitude of the move away from the fill, so it must be greater than zero — the protective side is
        /// the engine's to decide, not the caller's to signal with a negative number.
        /// </summary>
        public static BracketLegSpec OffsetFromFill(decimal offset)
        {
            if (offset <= 0m)
            {
                throw new ArgumentOutOfRangeException(nameof(offset), offset, "The leg offset must be greater than zero.");
            }

            return new BracketLegSpec(null, offset);
        }

        /// <summary>
        /// Gets the fill-relative distance this leg was given, or null when it was given as an absolute
        /// price. It is the per-share risk a risk-sizing model needs from a stop leg whose absolute price is
        /// not yet knowable at submit time (ADR 0025).
        /// </summary>
        internal decimal? Offset => _offset;

        /// <summary>
        /// Resolves this leg to the absolute price it rests at, given the actual (slippage-adjusted) entry
        /// fill and the sign that puts the leg on its protective side of that fill (-1 to subtract, +1 to add).
        /// An absolute leg resolves to itself and ignores both.
        /// </summary>
        internal decimal Resolve(decimal fillPrice, decimal offsetSign)
        {
            return _price ?? fillPrice + offsetSign * _offset.Value;
        }
    }
}
