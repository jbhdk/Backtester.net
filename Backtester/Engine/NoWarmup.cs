using System;
using System.Threading;
using System.Threading.Tasks;

namespace Backtester.Engine
{
    /// <summary>
    /// The absence of a warmup lead-in: the Data range equals the Test range, preserving the original
    /// single-range engine behaviour (ADR 0022).
    /// </summary>
    internal sealed class NoWarmup : Warmup
    {
        /// <summary>Returns the Test range's start unchanged — the fetch reaches back no further.</summary>
        public override Task<DateTime> ResolveDataStartAsync(string symbol, DateTime testFrom, string interval, CancellationToken ct)
        {
            return Task.FromResult(testFrom);
        }
    }
}
