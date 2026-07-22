using Backtester.Core;
using Backtester.Engine;

namespace Backtester.Optimization
{
    /// <summary>
    /// One Parameter set evaluated by a backtest, carrying its Performance stats and its Score. The
    /// underlying <see cref="BacktestResult"/> is carried for the best Trial (and for every Trial when the
    /// Optimizer is asked to retain all results); otherwise it is null.
    /// </summary>
    public class Trial
    {
        /// <summary>Initializes a new Trial for the given Parameter set, stats, score, eligibility, and optional result.</summary>
        public Trial(ParameterSet parameters, PerformanceStats stats, decimal score, bool eligible, BacktestResult backtestResult)
        {
            Parameters = parameters;
            Stats = stats;
            Score = score;
            Eligible = eligible;
            BacktestResult = backtestResult;
        }

        /// <summary>Gets the Parameter set this Trial was run with.</summary>
        public ParameterSet Parameters { get; }

        /// <summary>Gets the combined Performance stats this Trial's backtest produced.</summary>
        public PerformanceStats Stats { get; }

        /// <summary>Gets the Score the Objective assigned this Trial; Trials are ranked by it.</summary>
        public decimal Score { get; }

        /// <summary>
        /// Gets whether this Trial has enough Round trips to be eligible to win. A Trial with fewer Round
        /// trips than the Optimizer's configured minimum is ineligible: it is still ranked and shown, but it
        /// can never be <see cref="OptimizationResult.Best"/>.
        /// </summary>
        public bool Eligible { get; }

        /// <summary>
        /// Gets the full backtest result for this Trial, or null when it was not retained. The best Trial
        /// always carries it; other Trials carry it only when the Optimizer retained all results.
        /// </summary>
        public BacktestResult BacktestResult { get; }
    }
}
