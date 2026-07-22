using System.Collections.Generic;

namespace Backtester.Report
{
    /// <summary>
    /// The Score surface across a two-axis sweep: a heatmap cell per grid point so a reader can see whether
    /// the best Trial sits on a plateau or a lucky spike. Present on the report model only when exactly two
    /// Parameters vary. A pure display DTO — the two axes are pre-derived as ordered distinct values and
    /// every cell indexes into them.
    /// </summary>
    public class OptimizationScoreHeatmap
    {
        /// <summary>Gets or sets the name of the Parameter on the X axis (the columns).</summary>
        public string XParameterName { get; set; }

        /// <summary>Gets or sets the name of the Parameter on the Y axis (the rows).</summary>
        public string YParameterName { get; set; }

        /// <summary>
        /// Gets or sets the X axis's distinct values as display strings, in ascending order. A cell's
        /// <see cref="OptimizationHeatmapCell.XIndex"/> is a position in this list.
        /// </summary>
        public IReadOnlyList<string> XValues { get; set; }

        /// <summary>
        /// Gets or sets the Y axis's distinct values as display strings, in ascending order. A cell's
        /// <see cref="OptimizationHeatmapCell.YIndex"/> is a position in this list.
        /// </summary>
        public IReadOnlyList<string> YValues { get; set; }

        /// <summary>Gets or sets the charted cells, one per evaluated grid point.</summary>
        public IReadOnlyList<OptimizationHeatmapCell> Cells { get; set; }
    }
}
