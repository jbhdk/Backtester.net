using System.Collections.Generic;

namespace Backtester.Data
{
    using Backtester.Core;

    public class MarketSlice
    {
        public required IReadOnlyDictionary<string, Candle> BarsBySymbol { get; set; }
    }
}
