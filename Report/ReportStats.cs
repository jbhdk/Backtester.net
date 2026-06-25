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

        /// <summary>Gets or sets the number of round trips that closed with a positive P&amp;L.</summary>
        public int Winners { get; set; }

        /// <summary>Gets or sets the number of round trips that closed with a negative P&amp;L.</summary>
        public int Losers { get; set; }

        /// <summary>
        /// Gets or sets the number of round trips that closed at exactly zero P&amp;L (neither win nor loss).
        /// </summary>
        public int BreakEven { get; set; }

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

        /// <summary>Gets or sets the longest consecutive sequence of winning round trips.</summary>
        public int MaxConsecWins { get; set; }

        /// <summary>Gets or sets the buy-and-hold return over the run as a fraction (the benchmark, not the strategy).</summary>
        public decimal BuyHoldReturnPercent { get; set; }

        /// <summary>Gets or sets the annualised Sortino ratio.</summary>
        public decimal Sortino { get; set; }

        /// <summary>Gets or sets the Calmar ratio (CAGR over maximum drawdown).</summary>
        public decimal Calmar { get; set; }

        /// <summary>Gets or sets the recovery factor (net profit over maximum drawdown in currency).</summary>
        public decimal RecoveryFactor { get; set; }

        /// <summary>Gets or sets the mean depth of all drawdown episodes, as a fraction (0–1).</summary>
        public decimal AvgDrawdown { get; set; }

        /// <summary>Gets or sets the longest drawdown episode's duration, pre-formatted (e.g. "63d 5h").</summary>
        public string MaxDrawdownDuration { get; set; }

        /// <summary>Gets or sets the time from the deepest drawdown's trough to a new high, pre-formatted.</summary>
        public string TimeToRecover { get; set; }

        /// <summary>Gets or sets the median realized profit/loss across all round trips.</summary>
        public decimal MedianTrade { get; set; }

        /// <summary>Gets or sets the largest single winning round trip's profit.</summary>
        public decimal LargestWin { get; set; }

        /// <summary>Gets or sets the largest single losing round trip's loss (negative value).</summary>
        public decimal LargestLoss { get; set; }

        /// <summary>Gets or sets the average R multiple (expectancy in units of the average losing trade).</summary>
        public decimal AvgRMultiple { get; set; }

        /// <summary>Gets or sets the fraction of long round trips that were profitable (0–1).</summary>
        public decimal LongWinRate { get; set; }

        /// <summary>Gets or sets the fraction of short round trips that were profitable (0–1).</summary>
        public decimal ShortWinRate { get; set; }

        /// <summary>Gets or sets the fraction of bars on which at least one position was open (0–1).</summary>
        public decimal MarketExposure { get; set; }

        /// <summary>Gets or sets the time-weighted average gross capital deployed in open positions, in currency.</summary>
        public decimal AvgCapitalInvested { get; set; }

        /// <summary>Gets or sets the peak gross capital deployed in open positions on any single bar, in currency.</summary>
        public decimal MaxCapitalInvested { get; set; }

        /// <summary>Gets or sets the mean round-trip holding time, pre-formatted (e.g. "3d 14h").</summary>
        public string AvgTradeDuration { get; set; }

        /// <summary>Gets or sets the median round-trip holding time, pre-formatted.</summary>
        public string MedianTradeDuration { get; set; }

        /// <summary>Gets or sets the longest round-trip holding time, pre-formatted.</summary>
        public string LongestTradeDuration { get; set; }

        /// <summary>Gets or sets the shortest round-trip holding time, pre-formatted.</summary>
        public string ShortestTradeDuration { get; set; }
    }
}
