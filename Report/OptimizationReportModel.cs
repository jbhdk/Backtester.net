using System.Collections.Generic;

namespace Backtester.Report
{
    /// <summary>
    /// The serializable view model for an optimization report: the whole sweep rendered as a sortable
    /// leaderboard. A pure projection of an Optimization's ranked Trials, with every value the page
    /// renders pre-derived. Report-rendered like <see cref="ReportAnalysis"/>, so the Report project
    /// takes no dependency on the Optimization project that produces it.
    /// </summary>
    public class OptimizationReportModel
    {
        /// <summary>
        /// Gets or sets the swept Parameter names, in axis order — the leaderboard's Parameter column
        /// headers. Every row's <see cref="OptimizationTrialRow.ParameterValues"/> aligns to this order.
        /// Empty when the sweep varied no Parameters.
        /// </summary>
        public IReadOnlyList<string> ParameterNames { get; set; }

        /// <summary>Gets or sets the leaderboard rows, one per Trial, in the sweep's ranked order (best first).</summary>
        public IReadOnlyList<OptimizationTrialRow> Trials { get; set; }
    }
}
