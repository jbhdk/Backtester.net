using Backtester.Report.Toolkit;

namespace BacktesterTests.Report.Toolkit.Tests
{
    /// <summary>
    /// A fixture whose "MACD" members are declared in the opposite order to their <c>Order</c> values, so a
    /// builder that honours <c>Order</c> reverses declaration order. Drives the ascending row-order test for
    /// <see cref="ConfigurationCardBuilder"/>.
    /// </summary>
    public class OrderedSettings
    {
        /// <summary>Declared first but carries the higher <c>Order</c>, so it renders second.</summary>
        [ReportSetting("Fast period", "MACD", Order = 2)]
        public int FastPeriod { get; set; }

        /// <summary>Declared second but carries the lower <c>Order</c>, so it renders first.</summary>
        [ReportSetting("Slow period", "MACD", Order = 1)]
        public int SlowPeriod { get; set; }
    }
}
