using System.Text.Json.Serialization;

namespace Backtester.Analysis
{
    /// <summary>
    /// The wire shape of one Finding, matching the Analysis contract's JSON schema. Category and
    /// severity arrive as the contract's own strings and are mapped onto the report's enums.
    /// </summary>
    internal class AnalysisResponseFinding
    {
        /// <summary>Gets or sets the contract's category string, e.g. <c>"data quality"</c>.</summary>
        [JsonPropertyName("category")]
        public string Category { get; set; }

        /// <summary>Gets or sets the contract's severity string, e.g. <c>"medium"</c>.</summary>
        [JsonPropertyName("severity")]
        public string Severity { get; set; }

        /// <summary>Gets or sets the Finding's short headline.</summary>
        [JsonPropertyName("title")]
        public string Title { get; set; }

        /// <summary>Gets or sets what the numbers show.</summary>
        [JsonPropertyName("observation")]
        public string Observation { get; set; }

        /// <summary>Gets or sets what to change in response to the observation.</summary>
        [JsonPropertyName("recommendation")]
        public string Recommendation { get; set; }
    }
}
