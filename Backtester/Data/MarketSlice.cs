using System;
using System.Collections.Generic;

namespace Backtester.Data
{
    using Backtester.Core;

    public class MarketSlice
    {
        // The timestamp (UTC) for this slice
        public required DateTime Timestamp { get; set; }

        // Mapping of symbol -> latest available bar at `Timestamp` (may be null if absent)
        public required IReadOnlyDictionary<string, Candle> BarsBySymbol { get; set; }

        // Helper: whether a bar exists for a symbol in this slice
        public bool HasBar(string symbol) => BarsBySymbol != null && BarsBySymbol.ContainsKey(symbol) && BarsBySymbol[symbol] != null;
    }
}
