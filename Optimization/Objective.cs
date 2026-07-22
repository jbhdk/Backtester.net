using System;
using Backtester.Core;

namespace Backtester.Optimization
{
    /// <summary>
    /// The rule an Optimization ranks Trials by: a function over a Trial's combined (whole-run)
    /// <see cref="PerformanceStats"/> paired with a <see cref="OptimizationDirection"/>. The function reads
    /// the combined stats only — never Per-symbol stats — so ranking always reflects whole-run performance.
    /// Build one with <see cref="Maximize"/> or <see cref="Minimize"/>, or use a preset from <see cref="Objectives"/>.
    /// </summary>
    public class Objective
    {
        private readonly Func<PerformanceStats, decimal> _metric;

        /// <summary>Initializes a new Objective over the given metric function and ranking direction.</summary>
        private Objective(Func<PerformanceStats, decimal> metric, OptimizationDirection direction)
        {
            _metric = metric;
            Direction = direction;
        }

        /// <summary>Gets the direction this Objective ranks Trials in.</summary>
        public OptimizationDirection Direction { get; }

        /// <summary>Creates an Objective that ranks Trials from highest to lowest value of <paramref name="metric"/>.</summary>
        public static Objective Maximize(Func<PerformanceStats, decimal> metric)
        {
            return new Objective(metric, OptimizationDirection.Maximize);
        }

        /// <summary>Creates an Objective that ranks Trials from lowest to highest value of <paramref name="metric"/>.</summary>
        public static Objective Minimize(Func<PerformanceStats, decimal> metric)
        {
            return new Objective(metric, OptimizationDirection.Minimize);
        }

        /// <summary>
        /// Assigns a Trial its Score by reading the metric from its combined Performance stats. The raw metric
        /// value is returned regardless of direction; the direction governs ordering, not the Score itself.
        /// </summary>
        public decimal Score(PerformanceStats stats)
        {
            return _metric(stats);
        }
    }
}
