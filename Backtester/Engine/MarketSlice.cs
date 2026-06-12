using System.Collections.Generic;

namespace Backtester.Engine
{
    using Backtester.Core;

    public class MarketSlice
    {
        public IReadOnlyDictionary<string, Candle> BarsBySymbol { get; set; }
    }
}
