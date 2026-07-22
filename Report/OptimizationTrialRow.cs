using System.Collections.Generic;

namespace Backtester.Report
{
    /// <summary>
    /// One row of the optimization leaderboard: a single Trial projected for display. Carries the Trial's
    /// Score, its eligibility and best-in-sweep flags, and the compact risk-adjusted metrics the table
    /// compares Trials by. A pure display DTO — every value is pre-derived by the model builder.
    /// </summary>
    public class OptimizationTrialRow
    {
        /// <summary>Gets or sets the Trial's 1-based position in the sweep's ranking (rank 1 is the top-scoring Trial).</summary>
        public int Rank { get; set; }

        /// <summary>
        /// Gets or sets this Trial's Parameter values as display strings, aligned to the model's
        /// <see cref="OptimizationReportModel.ParameterNames"/> column order. Empty when no Parameters varied.
        /// </summary>
        public IReadOnlyList<string> ParameterValues { get; set; }

        /// <summary>Gets or sets the Score the Objective assigned this Trial; the leaderboard is ranked by it.</summary>
        public decimal Score { get; set; }

        /// <summary>
        /// Gets or sets whether this Trial is the best in the sweep — the highest-scoring eligible Trial the
        /// page highlights. Exactly one row is flagged when the sweep has an eligible winner; none when it
        /// does not.
        /// </summary>
        public bool IsBest { get; set; }

        /// <summary>
        /// Gets or sets whether this Trial had enough Round trips to be eligible to win. An ineligible Trial
        /// is still ranked and shown, but the page flags it as unable to become the best.
        /// </summary>
        public bool Eligible { get; set; }

        /// <summary>Gets or sets the number of completed Round trips this Trial's backtest produced.</summary>
        public int Trades { get; set; }

        /// <summary>Gets or sets the net profit after commissions and slippage, in currency.</summary>
        public decimal NetProfit { get; set; }

        /// <summary>Gets or sets the largest peak-to-trough decline in marked equity as a fraction (0–1).</summary>
        public decimal MaxDrawdown { get; set; }

        /// <summary>Gets or sets the annualised Sharpe ratio.</summary>
        public decimal Sharpe { get; set; }

        /// <summary>Gets or sets the annualised Sortino ratio.</summary>
        public decimal Sortino { get; set; }

        /// <summary>Gets or sets the Calmar ratio (CAGR over maximum drawdown).</summary>
        public decimal Calmar { get; set; }

        /// <summary>Gets or sets gross profit divided by absolute gross loss; zero when there are no losses.</summary>
        public decimal ProfitFactor { get; set; }

        /// <summary>Gets or sets the fraction of Round trips that were profitable (0–1).</summary>
        public decimal WinRate { get; set; }
    }
}
