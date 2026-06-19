using System.Collections.Generic;
using Backtester.Core;

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

        /// <summary>Gets or sets the strategy-exposed indicator series, each with its pane placement.</summary>
        public IReadOnlyList<IndicatorSeries> Indicators { get; set; }

        /// <summary>Gets or sets the portfolio-wide equity curve.</summary>
        public IReadOnlyList<ReportEquityPoint> EquityCurve { get; set; }

        /// <summary>
        /// Gets or sets the per-symbol candle series the run executed on.
        /// Key: symbol/ticker (string) -> the candle series for that symbol.
        /// </summary>
        public IReadOnlyDictionary<string, IReadOnlyList<Candle>> Candles { get; set; }

        /// <summary>Gets or sets the run-context section: inputs plus derived final equity and total return.</summary>
        public ReportRunInfo Run { get; set; }
    }
}
