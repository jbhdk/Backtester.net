using System.Collections.Generic;

namespace Backtester.Report
{
    /// <summary>
    /// One round trip's stepped stop-loss or take-profit line, shaped for the chart library: the
    /// pre-derived vertices of the leg's level over the position's holding window (a trailed stop steps
    /// up; a static level is flat), confined to entry→exit. The page draws it as a thin dashed stepped
    /// line on the price pane without deciding placement or colour.
    /// </summary>
    public class ChartBracketLevel
    {
        /// <summary>Gets or sets the symbol the line belongs to, so the page shows only the selected symbol's lines.</summary>
        public string Symbol { get; set; }

        /// <summary>Gets or sets the 1-based number of the round trip this line belongs to, linking it to its table row.</summary>
        public int RoundTripNumber { get; set; }

        /// <summary>Gets or sets which leg this line is (<c>"stopLoss"</c> or <c>"takeProfit"</c>), so the page colours it.</summary>
        public string Leg { get; set; }

        /// <summary>
        /// Gets or sets the stepped vertices of the level over the holding window: the level at entry,
        /// one point per move, and a terminal point at the exit so the step extends to the close.
        /// </summary>
        public IReadOnlyList<ChartLinePoint> Points { get; set; }
    }
}
