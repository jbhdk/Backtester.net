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
    }
}
