using System;

namespace Backtester.Core
{
    /// <summary>
    /// A neutral record of a bracket protective leg's price level at a point in time: emitted when the
    /// broker arms the leg (its initial level) and on each modify that trails or moves it. A passive fact
    /// the broker accumulates; the report projects a round trip's stepped stop/target line from the
    /// changes that fall inside its holding window (ADR 0014).
    /// </summary>
    public class BracketLevelChange
    {
        /// <summary>Gets or sets the ticker symbol the protective leg belongs to.</summary>
        public string Symbol { get; set; }

        /// <summary>Gets or sets the UTC bar timestamp at which the leg took this level.</summary>
        public DateTime Timestamp { get; set; }

        /// <summary>Gets or sets which protective leg this level belongs to (<see cref="BracketLeg.StopLoss"/> or <see cref="BracketLeg.TakeProfit"/>).</summary>
        public BracketLeg Leg { get; set; }

        /// <summary>Gets or sets the leg's trigger price (its level) as of this timestamp.</summary>
        public decimal Price { get; set; }

        /// <summary>Gets or sets the id of the working order whose level this is, so concurrent legs can be told apart later.</summary>
        public string OrderId { get; set; }
    }
}
