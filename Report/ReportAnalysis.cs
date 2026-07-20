using System.Collections.Generic;

namespace Backtester.Report
{
    /// <summary>
    /// A machine-generated critique of one run: a short summary plus the Findings it produced. Like the
    /// configuration cards it is caller-supplied — the report renders it, it never generates it.
    /// </summary>
    public class ReportAnalysis
    {
        /// <summary>Gets or sets the short prose summary of the run, rendered above the Findings.</summary>
        public string Summary { get; set; }

        /// <summary>Gets or sets the Findings, rendered in the order the section's severity grouping gives them.</summary>
        public IReadOnlyList<ReportFinding> Findings { get; set; }

        /// <summary>Gets or sets what produced the Analysis, rendered as the section's subtitle.</summary>
        public AnalysisProvenance Provenance { get; set; }
    }
}
