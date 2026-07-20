using System;

namespace Backtester.Report
{
    /// <summary>
    /// What produced an Analysis. The report renders it as the Analysis section's subtitle so the
    /// section is unmistakably machine-generated, and so a reader can tell a 7B local model's critique
    /// from a frontier model's long after the run (ADR 0019).
    /// </summary>
    public class AnalysisProvenance
    {
        /// <summary>Gets or sets the AI service that answered, e.g. <c>Ollama</c>.</summary>
        public string Service { get; set; }

        /// <summary>Gets or sets the model that answered, e.g. <c>qwen2.5:7b</c>.</summary>
        public string Model { get; set; }

        /// <summary>Gets or sets when the Analysis was produced, in UTC.</summary>
        public DateTime GeneratedAtUtc { get; set; }

        /// <summary>Gets or sets the version of the analysis package that produced the Analysis.</summary>
        public string PackageVersion { get; set; }
    }
}
