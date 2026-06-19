namespace Backtester.Report
{
    /// <summary>
    /// The performance-stats section of the report model. Mirrors the engine's <c>PerformanceStats</c>
    /// and adds the derived net-profit percentage.
    /// </summary>
    public class ReportStats
    {
        /// <summary>Gets or sets the net profit after commissions and slippage, in currency.</summary>
        public decimal NetProfit { get; set; }

        /// <summary>Gets or sets the net profit as a fraction of starting equity.</summary>
        public decimal NetProfitPercent { get; set; }

        /// <summary>Gets or sets the number of completed round trips.</summary>
        public int Trades { get; set; }

        /// <summary>Gets or sets the fraction of round trips that were profitable (0–1).</summary>
        public decimal WinRate { get; set; }

        /// <summary>Gets or sets gross profit divided by absolute gross loss; zero when there are no losses.</summary>
        public decimal ProfitFactor { get; set; }

        /// <summary>Gets or sets the average profit of winning round trips.</summary>
        public decimal AvgWin { get; set; }

        /// <summary>Gets or sets the average loss of losing round trips (negative value).</summary>
        public decimal AvgLoss { get; set; }

        /// <summary>Gets or sets the expected value per trade.</summary>
        public decimal Expectancy { get; set; }

        /// <summary>Gets or sets the largest peak-to-trough decline in marked equity as a fraction (0–1).</summary>
        public decimal MaxDrawdown { get; set; }

        /// <summary>Gets or sets the compound annual growth rate as a fraction.</summary>
        public decimal Cagr { get; set; }

        /// <summary>Gets or sets the annualised Sharpe ratio.</summary>
        public decimal Sharpe { get; set; }

        /// <summary>Gets or sets the longest consecutive sequence of losing round trips.</summary>
        public int MaxConsecLosses { get; set; }
    }
}
