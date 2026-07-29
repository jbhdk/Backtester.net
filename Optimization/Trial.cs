using Backtester.Core;
using Backtester.Engine;

namespace Backtester.Optimization
{
    /// <summary>
    /// One Parameter set evaluated by a backtest, carrying its Performance stats and its Score. The
    /// underlying <see cref="BacktestResult"/> is carried for the best Trial (and for every Trial when the
    /// Optimizer is asked to retain all results); otherwise it is null. A Trial whose configuration the
    /// code under test refused to run is a <em>Rejected trial</em> (ADR 0027): it carries only its
    /// Parameter set and the rejection's reason — no stats, no Score — and can never be the best.
    /// </summary>
    public class Trial
    {
        /// <summary>Initializes a new scored Trial for the given Parameter set, stats, score, eligibility, and optional result.</summary>
        public Trial(ParameterSet parameters, PerformanceStats stats, decimal score, bool eligible, BacktestResult backtestResult)
        {
            Parameters = parameters;
            Stats = stats;
            Score = score;
            Eligible = eligible;
            BacktestResult = backtestResult;
        }

        private Trial(ParameterSet parameters, string rejectionReason)
        {
            Parameters = parameters;
            RejectionReason = rejectionReason;
        }

        /// <summary>
        /// Creates a Rejected trial: one whose configuration the code under test refused to run, carrying
        /// the rejection's reason instead of stats and a Score.
        /// </summary>
        /// <param name="parameters">The Parameter set the rejected configuration was built from.</param>
        /// <param name="rejectionReason">The reason the configuration was refused, shown on the leaderboard row.</param>
        public static Trial Rejected(ParameterSet parameters, string rejectionReason)
        {
            return new Trial(parameters, rejectionReason);
        }

        /// <summary>Gets the Parameter set this Trial was run with.</summary>
        public ParameterSet Parameters { get; }

        /// <summary>Gets the combined Performance stats this Trial's backtest produced; null for a Rejected trial.</summary>
        public PerformanceStats Stats { get; }

        /// <summary>Gets the Score the Objective assigned this Trial; Trials are ranked by it. Zero and meaningless for a Rejected trial.</summary>
        public decimal Score { get; }

        /// <summary>
        /// Gets whether this Trial has enough Round trips to be eligible to win. A Trial with fewer Round
        /// trips than the Optimizer's configured minimum is ineligible: it is still ranked and shown, but it
        /// can never be <see cref="OptimizationResult.Best"/>. A Rejected trial is never eligible.
        /// </summary>
        public bool Eligible { get; }

        /// <summary>
        /// Gets the full backtest result for this Trial, or null when it was not retained. The best Trial
        /// always carries it; other Trials carry it only when the Optimizer retained all results.
        /// </summary>
        public BacktestResult BacktestResult { get; }

        /// <summary>
        /// Gets the reason this Trial's configuration was refused, or null for a scored Trial. Non-null
        /// exactly when the Trial is a Rejected trial.
        /// </summary>
        public string RejectionReason { get; }

        /// <summary>Gets whether this is a Rejected trial: its configuration was refused, so it carries no stats and no Score.</summary>
        public bool IsRejected => RejectionReason != null;
    }
}
