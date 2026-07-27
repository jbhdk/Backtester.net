using System;
using System.Collections.Generic;

namespace Backtester.Core
{
    /// <summary>
    /// Represents a single point-in-time cross-section of market data across all tracked symbols.
    /// </summary>
    public class MarketSlice
    {
        /// <summary>Gets or sets the UTC timestamp for this slice.</summary>
        public required DateTime Timestamp { get; set; }

        // Key: symbol/ticker (string) -> latest available bar at Timestamp (null if the symbol has no bar at this time)
        /// <summary>Gets or sets the per-symbol bar data available at this timestamp.</summary>
        public required IReadOnlyDictionary<string, Candle> BarsBySymbol { get; set; }

        /// <summary>Returns true if a non-null bar exists for the given symbol in this slice.</summary>
        public bool HasBar(string symbol)
        {
            return BarsBySymbol != null && BarsBySymbol.ContainsKey(symbol) && BarsBySymbol[symbol] != null;
        }

        /// <summary>
        /// Returns true when the symbol has a <em>genuine</em> bar at this slice's timestamp — one that
        /// actually printed now, not a value forward-filled from an earlier session because another symbol
        /// drove this timestamp. Orders may only fill, and the strategy is only invoked, on a real bar:
        /// acting on a forward-filled bar would trade (or decide) at a time the symbol never traded (issue #56).
        /// </summary>
        public bool HasRealBar(string symbol)
        {
            return HasBar(symbol) && BarsBySymbol[symbol].Timestamp == Timestamp;
        }
    }
}
