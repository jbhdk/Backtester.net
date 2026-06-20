namespace Backtester.Report
{
    /// <summary>
    /// A single point on the portfolio-wide equity curve, indexed by trade count: the cumulative
    /// realized equity after a given number of closed round trips. Point zero is the starting equity.
    /// </summary>
    public class ReportEquityPoint
    {
        /// <summary>Gets or sets the number of closed round trips at this point (0 = before any trade).</summary>
        public int Trade { get; set; }

        /// <summary>Gets or sets the cumulative realized equity after that many trades.</summary>
        public decimal Equity { get; set; }
    }
}
