using Backtester.Report.Toolkit;

namespace BacktesterTests.Report.Toolkit.Tests
{
    /// <summary>
    /// A fixture settings POCO used to drive <see cref="ConfigurationCardBuilder"/> tests. Deliberately
    /// not a real strategy parameter object, so the builder is proven strategy-agnostic.
    /// </summary>
    public class SampleSettings
    {
        /// <summary>The fast MACD period. Shares the "MACD" group with <see cref="SlowPeriod"/>.</summary>
        [ReportSetting("Fast period", "MACD")]
        public int FastPeriod { get; set; }

        /// <summary>The slow MACD period. Shares the "MACD" group with <see cref="FastPeriod"/>.</summary>
        [ReportSetting("Slow period", "MACD")]
        public int SlowPeriod { get; set; }
    }
}
