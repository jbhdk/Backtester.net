using System;
using System.Threading;
using System.Threading.Tasks;

namespace Backtester.Engine
{
    /// <summary>
    /// A warmup expressed as a period (a <see cref="TimeSpan"/>) subtracted from the Test range's start,
    /// so a caller can say "give me six months of lead-in" without computing a date (ADR 0022). A
    /// too-short period silently under-warms; an over-provisioned one is harmless.
    /// </summary>
    internal sealed class PeriodWarmup : Warmup
    {
        private readonly TimeSpan _period;

        /// <summary>Initializes a warmup that reaches <paramref name="period"/> back from the Test start.</summary>
        public PeriodWarmup(TimeSpan period)
        {
            _period = period;
        }

        /// <summary>Returns the Test range's start moved earlier by the warmup period.</summary>
        public override Task<DateTime> ResolveDataStartAsync(string symbol, DateTime testFrom, string interval, CancellationToken ct)
        {
            return Task.FromResult(testFrom - _period);
        }
    }
}
