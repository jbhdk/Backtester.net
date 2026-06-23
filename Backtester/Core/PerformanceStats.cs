using System;
using System.Collections.Generic;

namespace Backtester.Core
{
    /// <summary>
    /// Aggregated performance metrics computed at the end of a backtest run.
    /// </summary>
    public class PerformanceStats
    {
        /// <summary>Gets or sets the individual round trips that underpin these statistics.</summary>
        public IReadOnlyList<RoundTrip> RoundTrips { get; set; }

        /// <summary>Gets or sets the net profit after commissions and slippage.</summary>
        public decimal NetProfit { get; set; }

        /// <summary>Gets or sets the sum of profits from all winning round trips.</summary>
        public decimal GrossProfit { get; set; }

        /// <summary>Gets or sets the sum of losses from all losing round trips (negative value).</summary>
        public decimal GrossLoss { get; set; }

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

        /// <summary>Gets or sets the expected value per trade: WinRate * AvgWin + (1 - WinRate) * AvgLoss.</summary>
        public decimal Expectancy { get; set; }

        /// <summary>Gets or sets the largest peak-to-trough decline in marked equity as a fraction (0–1).</summary>
        public decimal MaxDrawdown { get; set; }

        /// <summary>Gets or sets the compound annual growth rate as a fraction.</summary>
        public decimal Cagr { get; set; }

        /// <summary>Gets or sets the annualised Sharpe ratio (assuming daily bars, risk-free rate = 0).</summary>
        public decimal Sharpe { get; set; }

        /// <summary>Gets or sets the longest consecutive sequence of losing round trips.</summary>
        public int MaxConsecLosses { get; set; }

        /// <summary>Gets or sets the longest consecutive sequence of winning round trips.</summary>
        public int MaxConsecWins { get; set; }

        /// <summary>
        /// Gets or sets the annualised Sortino ratio: like the Sharpe ratio but dividing by the downside
        /// deviation (the dispersion of negative bar returns only), so upside volatility is not penalised.
        /// </summary>
        public decimal Sortino { get; set; }

        /// <summary>Gets or sets the Calmar ratio: CAGR divided by the maximum drawdown fraction (zero when flat).</summary>
        public decimal Calmar { get; set; }

        /// <summary>
        /// Gets or sets the recovery factor: net profit divided by the maximum drawdown in currency
        /// (zero when there was no drawdown). Higher means profit was earned for less peak-to-trough pain.
        /// </summary>
        public decimal RecoveryFactor { get; set; }

        /// <summary>Gets or sets the mean depth of all drawdown episodes, as a fraction (0–1).</summary>
        public decimal AvgDrawdown { get; set; }

        /// <summary>Gets or sets the duration of the longest drawdown episode (peak to recovery, or to run end if never recovered).</summary>
        public TimeSpan MaxDrawdownDuration { get; set; }

        /// <summary>Gets or sets the time from the deepest drawdown's trough back to a new equity high (zero if never recovered).</summary>
        public TimeSpan TimeToRecover { get; set; }

        /// <summary>Gets or sets the median realized profit/loss across all round trips.</summary>
        public decimal MedianTrade { get; set; }

        /// <summary>Gets or sets the largest single winning round trip's profit (zero when there were no winners).</summary>
        public decimal LargestWin { get; set; }

        /// <summary>Gets or sets the largest single losing round trip's loss as a negative value (zero when there were no losers).</summary>
        public decimal LargestLoss { get; set; }

        /// <summary>
        /// Gets or sets the average R multiple: expectancy expressed in units of the average losing trade.
        /// No per-trade stop is modelled, so the average loss stands in for the risk (R) of a trade.
        /// </summary>
        public decimal AvgRMultiple { get; set; }

        /// <summary>Gets or sets the fraction of long round trips that were profitable (0–1).</summary>
        public decimal LongWinRate { get; set; }

        /// <summary>Gets or sets the fraction of short round trips that were profitable (0–1).</summary>
        public decimal ShortWinRate { get; set; }

        /// <summary>Gets or sets the fraction of bars on which at least one position was open (0–1).</summary>
        public decimal MarketExposure { get; set; }

        /// <summary>
        /// Gets or sets the time-weighted average gross capital deployed in open positions across all bars
        /// (flat bars count as zero), in currency.
        /// </summary>
        public decimal AvgCapitalInvested { get; set; }

        /// <summary>Gets or sets the peak gross capital deployed in open positions on any single bar, in currency.</summary>
        public decimal MaxCapitalInvested { get; set; }

        /// <summary>Gets or sets the mean holding time across all round trips.</summary>
        public TimeSpan AvgTradeDuration { get; set; }

        /// <summary>Gets or sets the median holding time across all round trips.</summary>
        public TimeSpan MedianTradeDuration { get; set; }

        /// <summary>Gets or sets the longest holding time of any round trip.</summary>
        public TimeSpan LongestTradeDuration { get; set; }

        /// <summary>Gets or sets the shortest holding time of any round trip.</summary>
        public TimeSpan ShortestTradeDuration { get; set; }
    }
}
