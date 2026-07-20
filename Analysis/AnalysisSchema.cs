namespace Backtester.Analysis
{
    /// <summary>
    /// The Analysis contract's JSON schema: the shape every answer must satisfy. It travels in the
    /// request so an Analysis client can hand it to its service's native structured-output mode, which
    /// is what makes contract violations rare by construction rather than caught by retry (ADR 0019).
    /// <para>
    /// The <c>additionalProperties: false</c> on the root object and on the Findings item is
    /// load-bearing: Claude's structured-output mode rejects any object schema that omits it, naming
    /// only the first offending object. Anything adding a nested object here needs the same treatment.
    /// </para>
    /// </summary>
    internal static class AnalysisSchema
    {
        /// <summary>The schema, sent as the required output shape of every Analysis request.</summary>
        public const string Json = """
{
  "type": "object",
  "properties": {
    "summary": {
      "type": "string"
    },
    "findings": {
      "type": "array",
      "items": {
        "type": "object",
        "properties": {
          "category": {
            "type": "string",
            "enum": [
              "risk",
              "sizing",
              "execution",
              "robustness",
              "data quality"
            ]
          },
          "severity": {
            "type": "string",
            "enum": [
              "high",
              "medium",
              "low",
              "strength"
            ]
          },
          "title": {
            "type": "string"
          },
          "observation": {
            "type": "string"
          },
          "recommendation": {
            "type": "string"
          }
        },
        "required": [
          "category",
          "severity",
          "title",
          "observation",
          "recommendation"
        ],
        "additionalProperties": false
      }
    }
  },
  "required": [
    "summary",
    "findings"
  ],
  "additionalProperties": false
}
""";
    }
}
