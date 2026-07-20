using System.Collections.Generic;
using System.Linq;
using Backtester.Report;

namespace Backtester.Analysis
{
    /// <summary>
    /// Maps an AI's answer onto the report's Analysis types. Stays behind the
    /// <see cref="ReportAnalyzer"/> seam: the mapping is asserted through the Analysis the analyzer
    /// returns.
    /// </summary>
    internal class AnalysisResponseMapper
    {
        /// <summary>
        /// Maps the supplied answer onto an Analysis carrying the supplied Provenance. The answer has
        /// already been validated, so every category and severity is one the vocabulary knows.
        /// </summary>
        public ReportAnalysis Map(AnalysisResponse response, AnalysisProvenance provenance)
        {
            return new ReportAnalysis
            {
                Summary = response.Summary,
                Findings = MapFindings(response.Findings),
                Provenance = provenance
            };
        }

        /// <summary>
        /// Maps the Findings and orders them by severity — High, Medium, Low, then Strength — so the
        /// report's section reads worst-first however the AI happened to order its answer. The sort is
        /// stable, so Findings sharing a severity keep the order the AI gave them.
        /// </summary>
        private static IReadOnlyList<ReportFinding> MapFindings(IReadOnlyList<AnalysisResponseFinding> findings)
        {
            List<ReportFinding> mapped = new();
            if (findings == null)
            {
                return mapped;
            }

            foreach (AnalysisResponseFinding finding in findings)
            {
                mapped.Add(new ReportFinding
                {
                    Category = AnalysisVocabulary.Categories[finding.Category],
                    Severity = AnalysisVocabulary.Severities[finding.Severity],
                    Title = finding.Title,
                    Observation = finding.Observation,
                    Recommendation = finding.Recommendation
                });
            }

            return mapped.OrderBy(finding => finding.Severity).ToList();
        }
    }
}
