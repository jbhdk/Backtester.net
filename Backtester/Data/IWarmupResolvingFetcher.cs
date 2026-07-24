using System;
using System.Threading;
using System.Threading.Tasks;

namespace Backtester.Data
{
    /// <summary>
    /// A warmup-capable fetcher: an <see cref="IHistoricalDataFetcher"/> that can also resolve a bar-count
    /// warmup lead-in (ADR 0022). "N bars before the Test start" cannot be expressed through
    /// <see cref="IHistoricalDataFetcher.FetchAsync"/> because the range start is exactly what is unknown, so
    /// this seam resolves it to an exact per-symbol Data start from the fetcher's own coverage and cached
    /// bars — or refuses when fewer than N bars exist above the symbol's Coverage floor.
    /// </summary>
    public interface IWarmupResolvingFetcher : IHistoricalDataFetcher
    {
        /// <summary>
        /// Resolves the exact Data-range start for <paramref name="symbol"/> that yields exactly
        /// <paramref name="warmupBars"/> bars before <paramref name="testFrom"/>. Throws
        /// <see cref="InsufficientWarmupBarsException"/> when fewer than that many bars are available above the
        /// symbol's Coverage floor, refusing rather than serving a silently short lead-in (ADR 0021).
        /// </summary>
        Task<DateTime> ResolveWarmupStartAsync(string symbol, DateTime testFrom, int warmupBars, string interval, CancellationToken ct = default);
    }
}
