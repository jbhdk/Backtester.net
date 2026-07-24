using System;

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
        /// Resolves the Data range's start from the Test range's start: how far back the fetch reaches.
        /// </summary>
        public abstract DateTime DataStart(DateTime testFrom);
    }
}
