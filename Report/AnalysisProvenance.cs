using System;

namespace Backtester.Report
{
    /// <summary>
    /// What produced an Analysis. The report renders it as the Analysis section's subtitle so the
    /// section is unmistakably machine-generated, and so a reader can tell one model's critique from
    /// another's long after the run (ADR 0019).
    /// </summary>
    public class AnalysisProvenance
    {
        /// <summary>Gets or sets the AI service that answered, e.g. <c>Claude</c>.</summary>
        public string Service { get; set; }

        /// <summary>Gets or sets the model that answered, e.g. <c>claude-sonnet-5</c>.</summary>
        public string Model { get; set; }

        /// <summary>Gets or sets when the Analysis was produced, in UTC.</summary>
        public DateTime GeneratedAtUtc { get; set; }
    }
}
