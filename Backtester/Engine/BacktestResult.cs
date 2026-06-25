using System;
using System.Collections.Generic;
using Backtester.Broker;
using Backtester.Core;

namespace Backtester.Engine
{
    /// <summary>
    /// Bundles everything a single backtest run produced and how it was configured: the run inputs
    /// (symbols, interval, requested date range, starting equity), the exact per-symbol candle history
    /// the engine ran its slices on, the run's portfolio, and the strategy's exposed indicators.
    /// Serves as the single source of truth for report generation (ADR 0008): a report can be produced
    /// from this result alone, with no separately-supplied run context.
    /// </summary>
    public class BacktestResult
    {
        /// <summary>
        /// Initializes a new result bundling the run's inputs, candle history, portfolio, and indicators.
        /// </summary>
        public BacktestResult(
            IReadOnlyDictionary<string, IReadOnlyList<Candle>> candleHistory,
            Portfolio portfolio,
            IReadOnlyList<Indicator> indicators,
            IReadOnlyList<string> symbols,
            string interval,
            DateTime fromUtc,
            DateTime toUtc,
            IReadOnlyList<RejectedOrder> rejectedOrders)
        {
            CandleHistory = candleHistory;
            Portfolio = portfolio;
            Indicators = indicators;
            Symbols = symbols;
            Interval = interval;
            FromUtc = fromUtc;
            ToUtc = toUtc;
            RejectedOrders = rejectedOrders;
        }

        /// <summary>
        /// Gets the exact per-symbol candle series the engine stepped through.
        /// Key: symbol/ticker (string) -> the candle series the engine ran on for that symbol.
        /// </summary>
        public IReadOnlyDictionary<string, IReadOnlyList<Candle>> CandleHistory { get; }

        /// <summary>Gets the portfolio as it stood at the end of the run.</summary>
        public Portfolio Portfolio { get; }

        /// <summary>
        /// Gets the composite indicators the strategy exposed during the run, collected by the engine from
        /// any <see cref="Backtester.Strategies.IIndicatorSource"/> strategy. Empty when none were exposed.
        /// </summary>
        public IReadOnlyList<Indicator> Indicators { get; }

        /// <summary>Gets the symbols the run covered, in the order they were requested.</summary>
        public IReadOnlyList<string> Symbols { get; }

        /// <summary>Gets the bar interval the run used (e.g. <c>"1d"</c>).</summary>
        public string Interval { get; }

        /// <summary>Gets the requested start of the run's date range (UTC).</summary>
        public DateTime FromUtc { get; }

        /// <summary>Gets the requested end of the run's date range (UTC).</summary>
        public DateTime ToUtc { get; }

        /// <summary>
        /// Gets the orders the broker declined during the run, in attempt order, each capturing what was
        /// attempted and why (e.g. rejected by the Reg-T margin gate for insufficient buying power).
        /// </summary>
        public IReadOnlyList<RejectedOrder> RejectedOrders { get; }

        /// <summary>
        /// Gets the equity the portfolio started with. Sourced from the run's <see cref="Portfolio"/> so it
        /// cannot diverge from the equity history the report derives final equity from.
        /// </summary>
        public decimal StartingEquity => Portfolio.StartingCash;
    }
}
