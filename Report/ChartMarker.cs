namespace Backtester.Report
{
    /// <summary>
    /// A pre-derived chart marker for a round trip's entry or exit, shaped for the chart library:
    /// the page renders it without deciding placement, colour, or label.
    /// </summary>
    public class ChartMarker
    {
        /// <summary>Gets or sets the symbol the marker belongs to, so the page can show only the selected symbol's markers.</summary>
        public string Symbol { get; set; }

        /// <summary>Gets or sets the marked bar's timestamp encoded as UTC seconds since the Unix epoch.</summary>
        public long Time { get; set; }

        /// <summary>Gets or sets the marker's placement relative to the bar (<c>"belowBar"</c> or <c>"aboveBar"</c>).</summary>
        public string Position { get; set; }

        /// <summary>Gets or sets the marker glyph (<c>"arrowUp"</c> for an entry, <c>"arrowDown"</c> for an exit).</summary>
        public string Shape { get; set; }

        /// <summary>Gets or sets the marker colour, derived from the round trip's win/loss outcome.</summary>
        public string Color { get; set; }

        /// <summary>Gets or sets the marker label: the round trip's profit/loss.</summary>
        public string Text { get; set; }
    }
}
