using System;
using System.Collections.Generic;

namespace Backtester.Report
{
    /// <summary>
    /// The run inputs the caller supplies to the report builder alongside the <c>BacktestResult</c>:
    /// the symbols, interval, requested date range, and starting equity. These describe how the run
    /// was configured and are not recoverable from the result alone.
    /// </summary>
    public class ReportRunContext
    {
        /// <summary>
        /// Initializes a new run context with the symbols, interval, date range, and starting equity.
        /// </summary>
        public ReportRunContext(IReadOnlyList<string> symbols, string interval, DateTime fromUtc, DateTime toUtc, decimal startingEquity)
        {
            Symbols = symbols;
            Interval = interval;
            FromUtc = fromUtc;
            ToUtc = toUtc;
            StartingEquity = startingEquity;
        }

        /// <summary>Gets the symbols the run covered.</summary>
        public IReadOnlyList<string> Symbols { get; }

        /// <summary>Gets the bar interval the run used (e.g. <c>"1d"</c>).</summary>
        public string Interval { get; }

        /// <summary>Gets the requested start of the run's date range (UTC).</summary>
        public DateTime FromUtc { get; }

        /// <summary>Gets the requested end of the run's date range (UTC).</summary>
        public DateTime ToUtc { get; }

        /// <summary>Gets the equity the portfolio started with.</summary>
        public decimal StartingEquity { get; }
    }
}
