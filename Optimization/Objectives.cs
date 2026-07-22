namespace Backtester.Optimization
{
    /// <summary>
    /// Named <see cref="Objective"/> presets for the common ranking choices, so callers need no boilerplate.
    /// Each preset pairs a single Performance metric with its natural direction.
    /// </summary>
    public static class Objectives
    {
        /// <summary>Maximizes the annualised Sharpe ratio. This is the Optimizer's default Objective.</summary>
        public static Objective Sharpe => Objective.Maximize(stats => stats.Sharpe);

        /// <summary>Maximizes net profit after commissions and slippage.</summary>
        public static Objective NetProfit => Objective.Maximize(stats => stats.NetProfit);

        /// <summary>Maximizes the Calmar ratio (CAGR over maximum drawdown).</summary>
        public static Objective Calmar => Objective.Maximize(stats => stats.Calmar);

        /// <summary>Minimizes the maximum drawdown fraction, favouring the shallowest peak-to-trough decline.</summary>
        public static Objective MinDrawdown => Objective.Minimize(stats => stats.MaxDrawdown);

        /// <summary>Maximizes the profit factor (gross profit over absolute gross loss).</summary>
        public static Objective ProfitFactor => Objective.Maximize(stats => stats.ProfitFactor);
    }
}
