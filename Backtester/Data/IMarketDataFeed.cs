using System;
using System.Collections.Generic;

namespace Backtester.Data
{
    using Core;

    public interface IMarketDataFeed
    {
        DateTime CurrentTime { get; }
        bool MoveNext();
        IReadOnlyList<Candle> GetBars(string symbol, int lookback);
    }
}
