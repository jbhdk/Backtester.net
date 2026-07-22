using System.Collections.Generic;

namespace Backtester.Optimization
{
    /// <summary>
    /// The produced artifact of an Optimization: the Trials ranked by Score (best first) and the best one.
    /// (The glossary term is "Optimization"; the type is named <see cref="OptimizationResult"/> to avoid a
    /// <c>Backtester.Optimization.Optimization</c> stutter.)
    /// </summary>
    public class OptimizationResult
    {
        /// <summary>Initializes a new result from the ranked Trials and the best one.</summary>
        public OptimizationResult(IReadOnlyList<Trial> trials, Trial best)
        {
            Trials = trials;
            Best = best;
        }

        /// <summary>Gets every Trial that was evaluated, ranked by Score with the best first.</summary>
        public IReadOnlyList<Trial> Trials { get; }

        /// <summary>Gets the best Trial, or null when no Trials were evaluated.</summary>
        public Trial Best { get; }
    }
}
