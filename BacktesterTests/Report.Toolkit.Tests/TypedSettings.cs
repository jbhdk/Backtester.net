using Backtester.Report.Toolkit;

namespace BacktesterTests.Report.Toolkit.Tests
{
    /// <summary>
    /// A fixture carrying a decimal, a boolean, and a null reference value, all in one group. Drives the
    /// value-formatting tests for <see cref="ConfigurationCardBuilder"/>.
    /// </summary>
    public class TypedSettings
    {
        /// <summary>A decimal setting, used to prove invariant-culture number formatting.</summary>
        [ReportSetting("Risk fraction", "Values")]
        public decimal RiskFraction { get; set; }

        /// <summary>A boolean setting, used to prove <c>True</c>/<c>False</c> rendering.</summary>
        [ReportSetting("Enabled", "Values")]
        public bool Enabled { get; set; }

        /// <summary>A reference-typed setting left null, used to prove empty-string rendering.</summary>
        [ReportSetting("Note", "Values")]
        public string Note { get; set; }
    }
}
