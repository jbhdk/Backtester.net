using System.Collections.Generic;

namespace Backtester.Report
{
    /// <summary>
    /// The serializable view model for an HTML backtest report. A pure projection of a
    /// <c>BacktestResult</c> (plus run context) with all values the page renders pre-derived.
    /// </summary>
    public class ReportModel
    {
        /// <summary>Gets or sets the performance-stats section.</summary>
        public ReportStats Stats { get; set; }

        /// <summary>Gets or sets the round trips with their derived return and holding time.</summary>
        public IReadOnlyList<ReportRoundTrip> RoundTrips { get; set; }

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
