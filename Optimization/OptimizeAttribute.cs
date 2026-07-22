using System;

namespace Backtester.Optimization
{
    /// <summary>
    /// Marks a Parameter — a property on a parameters class — for the Optimizer to vary. A numeric
    /// Parameter (<see cref="int"/> or <see cref="decimal"/>) sweeps from <see cref="Min"/> to
    /// <see cref="Max"/> inclusive in increments of <see cref="Step"/>; a <see cref="bool"/> Parameter
    /// uses the parameterless form and expands to <c>{ false, true }</c>. The range is carried as
    /// <see cref="double"/> because <see cref="decimal"/> is not a legal attribute-argument type; a
    /// decimal axis converts the bounds when it is built. Orthogonal to the report toolkit's
    /// <c>[ReportSetting]</c>: the two co-decorate a property without interacting.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class OptimizeAttribute : Attribute
    {
        /// <summary>
        /// Initializes a numeric Parameter axis swept from <paramref name="min"/> to
        /// <paramref name="max"/> inclusive in increments of <paramref name="step"/>.
        /// </summary>
        public OptimizeAttribute(double min, double max, double step)
        {
            Min = min;
            Max = max;
            Step = step;
            HasRange = true;
        }

        /// <summary>Initializes a boolean Parameter axis that expands to <c>{ false, true }</c>.</summary>
        public OptimizeAttribute()
        {
        }

        /// <summary>Gets the inclusive lower bound of a numeric axis.</summary>
        public double Min { get; }

        /// <summary>Gets the inclusive upper bound of a numeric axis.</summary>
        public double Max { get; }

        /// <summary>Gets the step increment of a numeric axis.</summary>
        public double Step { get; }

        /// <summary>
        /// Gets a value indicating whether a numeric range was supplied. False for the parameterless
        /// (boolean) form, which expands to <c>{ false, true }</c> instead of a range.
        /// </summary>
        public bool HasRange { get; }
    }
}
