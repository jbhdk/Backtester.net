using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Backtester.Core;

namespace Backtester.Data
{
    /// <summary>
    /// Fetches historical OHLCV candle data from an external or cached source.
    /// </summary>
    public interface IHistoricalDataProvider
    {
        /// <summary>
        /// Fetch candles for a symbol in the given time range and interval.
        /// Implementations must throw an informative exception if the provider cannot supply the requested symbol or interval.
        /// </summary>
        Task<IEnumerable<Candle>> FetchAsync(string symbol, DateTime fromUtc, DateTime toUtc, string interval, CancellationToken ct = default);
    }
}
