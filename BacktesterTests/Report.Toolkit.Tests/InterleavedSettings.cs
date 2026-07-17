using Backtester.Report.Toolkit;

namespace BacktesterTests.Report.Toolkit.Tests
{
    /// <summary>
    /// A fixture whose group members are declared out of contiguous order, and whose in-group declaration
    /// order is deliberately non-alphabetical. Drives grouping and ordering tests for
    /// <see cref="ConfigurationCardBuilder"/>.
    /// </summary>
    public class InterleavedSettings
    {
        /// <summary>Declared first; belongs to "MACD". Its label sorts after <see cref="FastPeriod"/> alphabetically.</summary>
        [ReportSetting("Slow period", "MACD")]
        public int SlowPeriod { get; set; }

        /// <summary>Declared between the two "MACD" members; belongs to "Risk".</summary>
        [ReportSetting("Risk fraction", "Risk")]
        public decimal RiskFraction { get; set; }

        /// <summary>Declared last; belongs to "MACD" and joins the card opened by <see cref="SlowPeriod"/>.</summary>
        [ReportSetting("Fast period", "MACD")]
        public int FastPeriod { get; set; }
    }
}
