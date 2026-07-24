using System;

namespace Backtester.Engine
{
    /// <summary>
    /// The absence of a warmup lead-in: the Data range equals the Test range, preserving the original
    /// single-range engine behaviour (ADR 0022).
    /// </summary>
    internal sealed class NoWarmup : Warmup
    {
        /// <summary>Returns the Test range's start unchanged — the fetch reaches back no further.</summary>
        public override DateTime DataStart(DateTime testFrom)
        {
            return testFrom;
        }
    }
}
