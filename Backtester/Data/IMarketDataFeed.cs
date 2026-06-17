using System;
using System.Collections.Generic;
using Backtester.Core;

namespace Backtester.Data
{
    /// <summary>
    /// Provides a bar-by-bar cursor over synchronized multi-symbol market data.
    /// </summary>
    public interface IMarketDataFeed
    {
        /// <summary>Gets the current global cursor timestamp (UTC).</summary>
        DateTime CurrentTime { get; }

        /// <summary>Advances the feed to the next timestamp. Returns false when all data has been consumed.</summary>
        bool Advance();

        /// <summary>Returns a snapshot of all symbol bars available at the current time cursor.</summary>
        MarketSlice GetCurrentSlice();

        /// <summary>Returns the last <paramref name="lookback"/> bars for <paramref name="symbol"/>, newest-first.</summary>
        IReadOnlyList<Candle> GetLookback(string symbol, int lookback);
    }
}
