using System;
using System.Threading;
using System.Threading.Tasks;
using Backtester.Data;

namespace Backtester.Engine
{
    /// <summary>
    /// A warmup expressed as a bar count — "N bars before the Test start" — resolved per symbol at fetch
    /// time through the <see cref="IWarmupResolvingFetcher"/> seam (ADR 0022). Because bar density differs
    /// between symbols, the same N resolves to a different calendar date for each; a symbol lacking N bars
    /// above its Coverage floor is refused with an <see cref="InsufficientWarmupBarsException"/> rather than
    /// run on a short lead-in.
    /// </summary>
    internal sealed class BarCountWarmup : Warmup
    {
        private readonly int _warmupBars;
        private readonly IWarmupResolvingFetcher _resolver;

        /// <summary>
        /// Initializes a bar-count warmup of <paramref name="warmupBars"/> bars, resolved through
        /// <paramref name="resolver"/>. The count must be positive.
        /// </summary>
        public BarCountWarmup(int warmupBars, IWarmupResolvingFetcher resolver)
        {
            if (warmupBars <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(warmupBars), warmupBars, "Warmup bar count must be positive.");
            }

            _warmupBars = warmupBars;
            _resolver = resolver;
        }

        /// <summary>Delegates to the fetcher seam to resolve the exact Data start for this symbol.</summary>
        public override Task<DateTime> ResolveDataStartAsync(string symbol, DateTime testFrom, string interval, CancellationToken ct)
        {
            return _resolver.ResolveWarmupStartAsync(symbol, testFrom, _warmupBars, interval, ct);
        }
    }
}
