using Backtester.Report.Toolkit;

namespace BacktesterTests.Report.Toolkit.Tests
{
    /// <summary>
    /// A fixture exercising <see cref="ReportSettingIgnoreAttribute"/> in both of its forms: a property that
    /// carries a display attribute yet is still excluded (ignore wins over presentation), and an
    /// un-attributed property that must not fall into the catch-all "Other" group.
    /// </summary>
    public class IgnoreSettings
    {
        /// <summary>The one property that survives into a card; anchors the "MACD" group.</summary>
        [ReportSetting("Fast period", "MACD")]
        public int FastPeriod { get; set; }

        /// <summary>Carries a display attribute but is ignored — must not appear in its "MACD" group.</summary>
        [ReportSetting("Secret period", "MACD")]
        [ReportSettingIgnore]
        public int SecretPeriod { get; set; }

        /// <summary>Ignored and un-attributed — must not surface in the catch-all "Other" group.</summary>
        [ReportSettingIgnore]
        public int UntidySecret { get; set; }
    }
}
