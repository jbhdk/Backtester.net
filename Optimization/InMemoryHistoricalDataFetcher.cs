using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Backtester.Core;
using Backtester.Data;

namespace Backtester.Optimization
{
    /// <summary>
    /// An <see cref="IHistoricalDataFetcher"/> that serves already-fetched bars from memory. The Optimizer
    /// fetches every symbol once through the real fetcher, wraps the series here, and hands this to every
    /// Trial's engine, so a sweep reads the data once and every Trial runs on identical bars (ADR 0005
    /// sanctions supplying data by faking the fetcher seam).
    /// </summary>
    public class InMemoryHistoricalDataFetcher : IHistoricalDataFetcher
    {
        // Key: symbol/ticker -> the pre-fetched candle series returned for that symbol on every request.
        private readonly IReadOnlyDictionary<string, IReadOnlyList<Candle>> _seriesBySymbol;

        /// <summary>Initializes a new in-memory fetcher over the given pre-fetched per-symbol series.</summary>
        public InMemoryHistoricalDataFetcher(IReadOnlyDictionary<string, IReadOnlyList<Candle>> seriesBySymbol)
        {
            _seriesBySymbol = seriesBySymbol;
        }

        /// <summary>
        /// Returns the pre-fetched series for the symbol, ignoring the range and interval (the series was
        /// fetched for them already). Returns an empty series for an unknown symbol.
        /// </summary>
        public Task<IReadOnlyList<Candle>> FetchAsync(string symbol, DateTime fromUtc, DateTime toUtc, string interval, CancellationToken ct = default)
        {
            if (_seriesBySymbol.TryGetValue(symbol, out IReadOnlyList<Candle> series))
            {
                return Task.FromResult(series);
            }

            return Task.FromResult<IReadOnlyList<Candle>>(Array.Empty<Candle>());
        }
    }
}
