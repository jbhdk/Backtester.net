using System.Collections.Generic;

namespace Backtester.Report
{
    /// <summary>
    /// The serializable view model for an HTML backtest report. A pure projection of a
    /// <c>BacktestResult</c> (plus run context) with all values the page renders pre-derived.
    /// </summary>
    public class ReportModel
    {
        /// <summary>Gets or sets the performance-stats section (all symbols combined).</summary>
        public ReportStats Stats { get; set; }

        // Key: symbol/ticker -> that symbol's standalone performance stats, for the report's per-symbol column.
        /// <summary>Gets or sets the per-symbol performance stats, keyed by symbol.</summary>
        public IReadOnlyDictionary<string, ReportStats> StatsBySymbol { get; set; }

        /// <summary>Gets or sets the round trips with their derived return and holding time.</summary>
        public IReadOnlyList<ReportRoundTrip> RoundTrips { get; set; }

        /// <summary>Gets or sets the orders the broker declined, surfaced in the trade log marked as rejected.</summary>
        public IReadOnlyList<ReportRejectedOrder> RejectedOrders { get; set; }

        /// <summary>Gets or sets the chart-ready strategy-exposed indicator series, each with its pane placement.</summary>
        public IReadOnlyList<ChartIndicator> Indicators { get; set; }

        /// <summary>Gets or sets the portfolio-wide equity curve, indexed by trade count.</summary>
        public IReadOnlyList<ReportEquityPoint> EquityCurve { get; set; }

        /// <summary>Gets or sets the chart-ready price section: per-symbol candle series the run executed on.</summary>
        public ReportChart Chart { get; set; }

        /// <summary>Gets or sets the run-context section: inputs plus derived final equity and total return.</summary>
        public ReportRunInfo Run { get; set; }
    }
}
