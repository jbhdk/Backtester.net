using Backtester.Report.Toolkit;

namespace BacktesterTests.Report.Toolkit.Tests
{
    /// <summary>
    /// A fixture with two "Risk" members sharing an <c>Order</c>, plus a lower-<c>Order</c> member declared
    /// last to force a real sort. Drives the stable tie-break test for <see cref="ConfigurationCardBuilder"/>:
    /// the low-<c>Order</c> member rises to the top while the tied pair keeps its declaration order.
    /// </summary>
    public class TiedOrderSettings
    {
        /// <summary>Declared first of the tied pair; shares <c>Order</c> 5 with <see cref="Gamma"/>.</summary>
        [ReportSetting("Beta", "Risk", Order = 5)]
        public int Beta { get; set; }

        /// <summary>Declared second of the tied pair; shares <c>Order</c> 5 with <see cref="Beta"/>.</summary>
        [ReportSetting("Gamma", "Risk", Order = 5)]
        public int Gamma { get; set; }

        /// <summary>Declared last but carries the lowest <c>Order</c>, so it renders first.</summary>
        [ReportSetting("Alpha", "Risk", Order = 1)]
        public int Alpha { get; set; }
    }
}
