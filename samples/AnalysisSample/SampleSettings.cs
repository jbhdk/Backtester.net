using Backtester.Report.Toolkit;

namespace AnalysisSample
{
    /// <summary>
    /// The run's parameters, attributed so the report toolkit can reflect them into configuration
    /// cards. The same cards reach the Analysis digest, which is why the Analyzer can comment on a
    /// setting at all: it only ever sees the settings the caller attached to the model.
    /// </summary>
    public class SampleSettings
    {
        /// <summary>Gets or sets the fast moving-average period, in bars.</summary>
        [ReportSetting("Fast period", "Strategy", Order = 1)]
        public int FastPeriod { get; set; }

        /// <summary>Gets or sets the slow moving-average period, in bars.</summary>
        [ReportSetting("Slow period", "Strategy", Order = 2)]
        public int SlowPeriod { get; set; }

        /// <summary>Gets or sets the fixed quantity every order is sized to, in shares.</summary>
        [ReportSetting("Order size (shares)", "Execution", Order = 1)]
        public int OrderSize { get; set; }

        /// <summary>Gets or sets the commission charged per share, in account currency.</summary>
        [ReportSetting("Commission per share", "Execution", Order = 2)]
        public decimal CommissionPerShare { get; set; }

        /// <summary>Gets or sets the slippage applied to every fill, in account currency per share.</summary>
        [ReportSetting("Slippage per share", "Execution", Order = 3)]
        public decimal SlippagePerShare { get; set; }

        /// <summary>Gets or sets the starting equity of the portfolio, in account currency.</summary>
        [ReportSetting("Starting equity", "Portfolio", Order = 1)]
        public decimal StartingEquity { get; set; }
    }
}
