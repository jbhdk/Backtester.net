using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Backtester.Core;
using Backtester.Data;

namespace Backtester.Optimization
{
    /// <summary>
    /// An <see cref="IWarmupResolvingFetcher"/> that serves already-fetched bars from memory. The Optimizer
    /// fetches every symbol once through the real fetcher, wraps the series here, and hands this to every
    /// Trial's engine, so a sweep reads the data once and every Trial runs on identical bars (ADR 0005
    /// sanctions supplying data by faking the fetcher seam).
    /// </summary>
    public class InMemoryHistoricalDataFetcher : IWarmupResolvingFetcher
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

        /// <summary>
        /// Resolves the Data-range start for the symbol that yields exactly <paramref name="warmupBars"/> bars
        /// before <paramref name="testFrom"/> from the pre-fetched series (the series is the trusted set, so
        /// there is no separate floor). Throws <see cref="InsufficientWarmupBarsException"/> when fewer than
        /// that many bars precede the Test start, refusing rather than serving a short lead-in (ADR 0022).
        /// </summary>
        public Task<DateTime> ResolveWarmupStartAsync(string symbol, DateTime testFrom, int warmupBars, string interval, CancellationToken ct = default)
        {
            if (warmupBars <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(warmupBars), warmupBars, "Warmup bar count must be positive.");
            }

            List<Candle> eligible = _seriesBySymbol.TryGetValue(symbol, out IReadOnlyList<Candle> series)
                ? series.Where(candle => candle.Timestamp < testFrom).OrderBy(candle => candle.Timestamp).ToList()
                : new List<Candle>();

            if (eligible.Count < warmupBars)
            {
                throw new InsufficientWarmupBarsException(symbol, warmupBars, eligible.Count, interval);
            }

            return Task.FromResult(eligible[eligible.Count - warmupBars].Timestamp);
        }
    }
}
