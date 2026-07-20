using System.Collections.Generic;
using Backtester.Report;

namespace Backtester.Analysis
{
    /// <summary>
    /// The Analysis contract's category and severity vocabulary: the strings an AI may use and the
    /// report enums they mean. It is the single place the vocabulary is spelled, so what the validator
    /// admits and what the mapper can map cannot drift apart — a category admitted but unmappable would
    /// fail after validation, which is precisely what strict validation exists to prevent.
    /// </summary>
    internal static class AnalysisVocabulary
    {
        /// <summary>Key: the contract's category string as the schema spells it. Value: the report's category.</summary>
        public static readonly IReadOnlyDictionary<string, FindingCategory> Categories = new Dictionary<string, FindingCategory>
        {
            { "risk", FindingCategory.Risk },
            { "sizing", FindingCategory.Sizing },
            { "execution", FindingCategory.Execution },
            { "robustness", FindingCategory.Robustness },
            { "data quality", FindingCategory.DataQuality }
        };

        /// <summary>Key: the contract's severity string as the schema spells it. Value: the report's severity.</summary>
        public static readonly IReadOnlyDictionary<string, FindingSeverity> Severities = new Dictionary<string, FindingSeverity>
        {
            { "high", FindingSeverity.High },
            { "medium", FindingSeverity.Medium },
            { "low", FindingSeverity.Low },
            { "strength", FindingSeverity.Strength }
        };
    }
}
