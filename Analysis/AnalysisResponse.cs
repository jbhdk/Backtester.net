using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Backtester.Analysis
{
    /// <summary>
    /// The wire shape of an AI's answer, matching the Analysis contract's JSON schema. It is the
    /// untrusted form of an Analysis: it is deserialized, validated, and only then mapped onto a
    /// <see cref="Backtester.Report.ReportAnalysis"/>.
    /// </summary>
    internal class AnalysisResponse
    {
        /// <summary>Gets or sets the short prose summary of the run.</summary>
        [JsonPropertyName("summary")]
        public string Summary { get; set; }

        /// <summary>Gets or sets the Findings, in whatever order the AI produced them.</summary>
        [JsonPropertyName("findings")]
        public List<AnalysisResponseFinding> Findings { get; set; }
    }
}
