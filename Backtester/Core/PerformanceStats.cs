namespace Backtester.Core
{
    /// <summary>
    /// Aggregated performance metrics computed at the end of a backtest run.
    /// </summary>
    public class PerformanceStats
    {
        /// <summary>Gets or sets the net profit after commissions and slippage.</summary>
        public decimal NetProfit { get; set; }

        /// <summary>Gets or sets the sum of all profitable trade returns.</summary>
        public decimal GrossProfit { get; set; }

        /// <summary>Gets or sets the sum of all losing trade returns.</summary>
        public decimal GrossLoss { get; set; }
    }
}
