using Backtester.Optimization;
using Backtester.Report.Toolkit;

namespace OptimizationSample
{
    /// <summary>
    /// The swept strategy Parameters, attributed twice over. <c>[Optimize]</c> declares the Fast and Slow
    /// moving-average periods as the two axes the Optimizer sweeps; <c>[ReportSetting]</c> lets the winning
    /// Trial's Parameters render as configuration cards on its single-run report. The two attributes are
    /// orthogonal — they co-decorate a property without interacting — which is exactly what the
    /// attributes-first authoring path this sample documents relies on.
    /// </summary>
    public class MovingAverageCrossParameters
    {
        /// <summary>Gets or sets the fast moving-average period, in bars — the first swept axis.</summary>
        [Optimize(5, 15, 5)]
        [ReportSetting("Fast period", "Strategy", Order = 1)]
        public int FastPeriod { get; set; }

        /// <summary>Gets or sets the slow moving-average period, in bars — the second swept axis.</summary>
        [Optimize(20, 40, 10)]
        [ReportSetting("Slow period", "Strategy", Order = 2)]
        public int SlowPeriod { get; set; }
    }
}
