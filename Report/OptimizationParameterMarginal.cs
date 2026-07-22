using System.Collections.Generic;

namespace Backtester.Report
{
    /// <summary>
    /// One Parameter's marginal view of the Score surface: for each distinct value of the Parameter, the
    /// Score summarised over the Trials that share it. The report carries one of these per varying Parameter
    /// when more than two Parameters vary — the higher-dimensional alternative to the two-axis
    /// <see cref="OptimizationScoreHeatmap"/>. A pure display DTO.
    /// </summary>
    public class OptimizationParameterMarginal
    {
        /// <summary>Gets or sets the name of the Parameter this marginal profiles.</summary>
        public string ParameterName { get; set; }

        /// <summary>Gets or sets the marginal points, one per distinct value of the Parameter, in ascending value order.</summary>
        public IReadOnlyList<OptimizationMarginalPoint> Points { get; set; }
    }
}
