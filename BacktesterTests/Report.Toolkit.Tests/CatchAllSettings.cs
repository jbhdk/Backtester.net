using Backtester.Report.Toolkit;

namespace BacktesterTests.Report.Toolkit.Tests
{
    /// <summary>
    /// A fixture whose first-declared property carries no <see cref="ReportSettingAttribute"/>, followed by
    /// an attributed group. Drives the catch-all "Other" tests for <see cref="ConfigurationCardBuilder"/>:
    /// the un-attributed property must surface in "Other", and "Other" must render last despite its member
    /// being declared before the attributed group.
    /// </summary>
    public class CatchAllSettings
    {
        /// <summary>Declared first and un-attributed; must fall into the catch-all "Other" group, labelled "UntidyValue".</summary>
        public int UntidyValue { get; set; }

        /// <summary>Declared after the un-attributed member; belongs to the curated "MACD" group.</summary>
        [ReportSetting("Fast period", "MACD")]
        public int FastPeriod { get; set; }
    }
}
