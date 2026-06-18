using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Backtester.Core;

namespace Backtester.Data
{
    /// <summary>
    /// Returns historical candle data for a symbol, serving from a local cache when possible
    /// and fetching missing data from an underlying provider.
    /// </summary>
    public interface IHistoricalDataFetcher
    {
        /// <summary>
        /// Returns candles for the symbol and date range, fetching from the underlying provider only when the cache is absent,
        /// stale, or does not cover the full requested range.
        /// </summary>
        Task<IReadOnlyList<Candle>> FetchAsync(string symbol, DateTime fromUtc, DateTime toUtc, string interval, CancellationToken ct = default);
    }
}
