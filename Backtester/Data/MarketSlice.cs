using System;
using System.Collections.Generic;
using Backtester.Core;

namespace Backtester.Data
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

    }
}
