using System;
using System.Threading;
using System.Threading.Tasks;

namespace Backtester.Engine
{
    /// <summary>
    /// A run's optional lead-in ahead of its Test range (ADR 0022): the stretch of bars added to the
    /// front of the Data range so a strategy's indicators are already warm on the first Test bar. A
    /// polymorphic value object so a future warmup form is a new subclass rather than a branch; kept
    /// internal and exercised only through the <see cref="Engine"/> public API.
    /// </summary>
    internal abstract class Warmup
    {
        /// <summary>Gets the shared "no warmup" value, for which the Data range equals the Test range.</summary>
        public static Warmup None { get; } = new NoWarmup();

        /// <summary>
        /// Resolves the Data range's start for one symbol: how far back the fetch reaches ahead of the Test
        /// range start. Per-symbol and asynchronous so a bar-count warmup can resolve "N bars before the Test
        /// start" against the fetcher's own coverage; the period, absolute, and no-warmup forms resolve to the
        /// same date for every symbol and ignore <paramref name="symbol"/> and <paramref name="interval"/>.
        /// </summary>
        public abstract Task<DateTime> ResolveDataStartAsync(string symbol, DateTime testFrom, string interval, CancellationToken ct);
    }
}
