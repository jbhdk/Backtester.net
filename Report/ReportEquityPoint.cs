using System;

namespace Backtester.Report
{
    /// <summary>
    /// A single point on the portfolio-wide equity curve: marked equity at a bar timestamp.
    /// </summary>
    public class ReportEquityPoint
    {
        /// <summary>Gets or sets the UTC timestamp of this equity sample.</summary>
        public DateTime Timestamp { get; set; }

        /// <summary>Gets or sets the mark-to-market portfolio equity at this timestamp.</summary>
        public decimal Equity { get; set; }
    }
}
