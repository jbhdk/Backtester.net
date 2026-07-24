using System;

namespace Backtester.Engine
{
    /// <summary>
    /// A warmup expressed as an absolute start date, pinning the Data range's start exactly (ADR 0022).
    /// The start is guarded to be no later than the Test range's start, so the lead-in only ever reaches
    /// earlier; an over-provisioned (very early) start is harmless, while a start below a symbol's Coverage
    /// floor surfaces the existing <c>DataCoverageException</c> when the fetch is attempted.
    /// </summary>
    internal sealed class AbsoluteWarmup : Warmup
    {
        private readonly DateTime _warmupStart;

        /// <summary>
        /// Initializes an absolute warmup pinned to <paramref name="warmupStart"/>, rejecting a start later
        /// than <paramref name="testFrom"/> since the Data range may only reach back before the Test range.
        /// </summary>
        public AbsoluteWarmup(DateTime warmupStart, DateTime testFrom)
        {
            if (warmupStart > testFrom)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(warmupStart),
                    warmupStart,
                    "Warmup start must be on or before the Test range start.");
            }

            _warmupStart = warmupStart;
        }

        /// <summary>Returns the pinned warmup start — the Data range begins exactly there.</summary>
        public override DateTime DataStart(DateTime testFrom)
        {
            return _warmupStart;
        }
    }
}
