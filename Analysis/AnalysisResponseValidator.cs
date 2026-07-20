using System.Collections.Generic;
using System.Globalization;

namespace Backtester.Analysis
{
    /// <summary>
    /// Checks an AI's answer against the Analysis contract. It validates strictly and never repairs: a
    /// value the contract does not define is a violation, not something to coerce to the nearest match
    /// (ADR 0019). Stays behind the <see cref="ReportAnalyzer"/> seam.
    /// </summary>
    internal class AnalysisResponseValidator
    {
        /// <summary>
        /// Returns the first violation the supplied answer commits, or null when it satisfies the
        /// contract.
        /// </summary>
        public string Validate(AnalysisResponse response)
        {
            if (response == null)
            {
                return "The answer is empty; the contract requires an object carrying a summary and findings.";
            }

            if (string.IsNullOrWhiteSpace(response.Summary))
            {
                return "summary: the contract requires it, and it is missing or empty.";
            }

            if (response.Findings == null)
            {
                return "findings: the contract requires it, and it is missing.";
            }

            for (int index = 0; index < response.Findings.Count; index++)
            {
                AnalysisResponseFinding finding = response.Findings[index];
                if (finding == null)
                {
                    return string.Format(
                        CultureInfo.InvariantCulture,
                        "findings[{0}]: the contract requires a Finding here, and it is empty.",
                        index);
                }

                if (finding.Category == null || !AnalysisVocabulary.Categories.ContainsKey(finding.Category))
                {
                    return Violation(index, "category", finding.Category, AnalysisVocabulary.Categories.Keys);
                }

                if (finding.Severity == null || !AnalysisVocabulary.Severities.ContainsKey(finding.Severity))
                {
                    return Violation(index, "severity", finding.Severity, AnalysisVocabulary.Severities.Keys);
                }

                if (string.IsNullOrWhiteSpace(finding.Title))
                {
                    return Missing(index, "title");
                }

                if (string.IsNullOrWhiteSpace(finding.Observation))
                {
                    return Missing(index, "observation");
                }

                if (string.IsNullOrWhiteSpace(finding.Recommendation))
                {
                    return Missing(index, "recommendation");
                }
            }

            return null;
        }

        /// <summary>
        /// Describes a Finding missing a field the contract requires. The Finding is rejected rather than
        /// dropped: a reader cannot tell a silently discarded Finding from one the AI never wrote.
        /// </summary>
        private static string Missing(int index, string field)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "findings[{0}].{1}: the contract requires it, and it is missing or empty.",
                index,
                field);
        }

        /// <summary>
        /// Describes a Finding's field carrying a value the contract does not define, naming the value the
        /// AI chose and the values it may choose from, so the retry has everything it needs to correct
        /// itself.
        /// </summary>
        private static string Violation(int index, string field, string value, IEnumerable<string> allowed)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "findings[{0}].{1}: \"{2}\" is not one of {3}.",
                index,
                field,
                value,
                string.Join(", ", allowed));
        }
    }
}
