using System;
using System.Collections.Generic;

namespace Backtester.Data
{
    using Core;

    public interface IMarketDataFeed
    {
        // Current global cursor (UTC)
        DateTime CurrentTime { get; }

        // Advance the feed to the next timestamp. Returns false at end-of-data.
        bool Advance();

        // Return a snapshot representing the current time cursor.
        MarketSlice GetCurrentSlice();

        // Return the last `lookback` bars for `symbol`, newest-first.
        IReadOnlyList<Candle> GetLookback(string symbol, int lookback);
    }
}
