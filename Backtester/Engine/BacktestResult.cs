using System.Collections.Generic;
using Backtester.Core;

namespace Backtester.Engine
{
    /// <summary>
    /// Bundles everything a single backtest run produced: the exact per-symbol candle history the
    /// engine ran its slices on, the run's portfolio, and the strategy's exposed indicator series.
    /// Serves as the single source of truth for report generation (ADR 0008).
    /// </summary>
    public class BacktestResult
    {
        /// <summary>
        /// Initializes a new result bundling the run's candle history, portfolio, and indicator series.
        /// </summary>
        public BacktestResult(
            IReadOnlyDictionary<string, IReadOnlyList<Candle>> candleHistory,
            Portfolio portfolio,
            IReadOnlyList<object> indicatorSeries)
        {
            CandleHistory = candleHistory;
            Portfolio = portfolio;
            IndicatorSeries = indicatorSeries;
        }

        /// <summary>
        /// Gets the exact per-symbol candle series the engine stepped through.
        /// Key: symbol/ticker (string) -> the candle series the engine ran on for that symbol.
        /// </summary>
        public IReadOnlyDictionary<string, IReadOnlyList<Candle>> CandleHistory { get; }

        /// <summary>Gets the portfolio as it stood at the end of the run.</summary>
        public Portfolio Portfolio { get; }

        /// <summary>
        /// Gets the indicator series the strategy exposed during the run. Empty until the exposure
        /// seam lands; the element type is firmed up by that follow-up (ADR 0007).
        /// </summary>
        public IReadOnlyList<object> IndicatorSeries { get; }
    }
}
