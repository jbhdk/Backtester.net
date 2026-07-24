using System;

namespace Backtester.Report
{
    /// <summary>
    /// One round trip in the report, with its raw fields plus the derived return percentage and a
    /// compactly formatted holding time.
    /// </summary>
    public class ReportRoundTrip
    {
        /// <summary>Gets or sets the 1-based ordinal of this round trip in the run.</summary>
        public int Number { get; set; }

        /// <summary>Gets or sets the ticker symbol traded.</summary>
        public string Symbol { get; set; }

        /// <summary>Gets or sets the direction of the round trip as a page-friendly string (<c>"Long"</c> or <c>"Short"</c>).</summary>
        public string Direction { get; set; }

        /// <summary>Gets or sets the UTC timestamp the position was entered.</summary>
        public DateTime EntryTime { get; set; }

        /// <summary>Gets or sets the UTC timestamp the position was exited.</summary>
        public DateTime ExitTime { get; set; }

        /// <summary>Gets or sets the volume-weighted average entry price.</summary>
        public decimal EntryPrice { get; set; }

        /// <summary>Gets or sets the exit fill price.</summary>
        public decimal ExitPrice { get; set; }

        /// <summary>Gets or sets the number of shares exited.</summary>
        public int Quantity { get; set; }

        /// <summary>Gets or sets the realized profit/loss for this round trip.</summary>
        public decimal RealizedPnL { get; set; }

        /// <summary>Gets or sets the price return as a fraction: (exit − entry) / entry.</summary>
        public decimal ReturnPercent { get; set; }

        /// <summary>
        /// Gets or sets the realized profit in units of initial risk (<c>RealizedPnL / InitialRisk</c>),
        /// e.g. <c>2</c> for a +2R win. Null when the round trip has no initial risk (its entry declared
        /// no protective stop), in which case the report shows no R.
        /// </summary>
        public decimal? RMultiple { get; set; }

        /// <summary>Gets or sets the holding time formatted compactly (e.g. <c>"5d 6h"</c>).</summary>
        public string TimeHeld { get; set; }

        /// <summary>Gets or sets why the round trip closed, as a page-friendly string (<c>"Take-profit"</c>, <c>"Stop-loss"</c>, or <c>"Signal"</c>).</summary>
        public string ExitReason { get; set; }
    }
}
