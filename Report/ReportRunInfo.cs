using System;
using System.Collections.Generic;

namespace Backtester.Report
{
    /// <summary>
    /// The run-context section of the report model: the run's inputs plus the derived final equity
    /// and total return that the raw performance stats do not carry.
    /// </summary>
    public class ReportRunInfo
    {
        /// <summary>Gets or sets the symbols the run covered.</summary>
        public IReadOnlyList<string> Symbols { get; set; }

        /// <summary>Gets or sets the bar interval the run used (e.g. <c>"1d"</c>).</summary>
        public string Interval { get; set; }

        /// <summary>Gets or sets the requested start of the run's date range (UTC).</summary>
        public DateTime FromUtc { get; set; }

        /// <summary>Gets or sets the requested end of the run's date range (UTC).</summary>
        public DateTime ToUtc { get; set; }

        /// <summary>Gets or sets the equity the portfolio started with.</summary>
        public decimal StartingEquity { get; set; }

        /// <summary>Gets or sets the marked equity at the end of the run.</summary>
        public decimal FinalEquity { get; set; }

        /// <summary>Gets or sets the total return over the run as a fraction: (final − starting) / starting.</summary>
        public decimal TotalReturnPercent { get; set; }
    }
}
