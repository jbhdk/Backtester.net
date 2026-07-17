using System.Collections.Generic;

namespace Backtester.Report
{
    /// <summary>
    /// A caller-supplied configuration card for the report: a titled table of pre-formatted text. The
    /// report treats every cell as opaque display text — it applies no typing, formatting, or styling.
    /// </summary>
    public class ReportCard
    {
        /// <summary>Gets or sets the card's heading. When null or empty the card renders no heading.</summary>
        public string Title { get; set; }

        /// <summary>
        /// Gets or sets the optional column headers. When null or empty the card renders no header row.
        /// </summary>
        public IReadOnlyList<string> Headers { get; set; }

        /// <summary>
        /// Gets or sets the card's rows, each an ordered list of cell strings. A card with no rows is not
        /// rendered. Rows need not share a length — the report renders each exactly as supplied.
        /// </summary>
        public IReadOnlyList<IReadOnlyList<string>> Rows { get; set; }
    }
}
