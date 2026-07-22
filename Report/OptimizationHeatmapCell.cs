namespace Backtester.Report
{
    /// <summary>
    /// One cell of the Score heatmap: the Score of the Trial at a single grid point, addressed by its
    /// position on the two varying axes. A pure display DTO — <see cref="XIndex"/> and <see cref="YIndex"/>
    /// index into the heatmap's <see cref="OptimizationScoreHeatmap.XValues"/> and
    /// <see cref="OptimizationScoreHeatmap.YValues"/>, so the renderer places the cell without re-deriving
    /// the axes.
    /// </summary>
    public class OptimizationHeatmapCell
    {
        /// <summary>Gets or sets the cell's column: an index into the heatmap's ordered X-axis values.</summary>
        public int XIndex { get; set; }

        /// <summary>Gets or sets the cell's row: an index into the heatmap's ordered Y-axis values.</summary>
        public int YIndex { get; set; }

        /// <summary>Gets or sets the Score charted at this grid point.</summary>
        public decimal Score { get; set; }
    }
}
