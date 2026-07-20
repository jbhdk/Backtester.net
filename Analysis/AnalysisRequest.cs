namespace Backtester.Analysis
{
    /// <summary>
    /// One request to an Analysis client: the instructions, the Analysis digest, and the output shape
    /// the answer must satisfy. The client carries this to its service unchanged — it decides nothing
    /// about what is asked.
    /// </summary>
    public class AnalysisRequest
    {
        /// <summary>Gets or sets the instructions defining the analyst role and the Finding vocabulary.</summary>
        public string SystemPrompt { get; set; }

        /// <summary>Gets or sets the user message: the rendered Analysis digest plus any caller guidance.</summary>
        public string UserPrompt { get; set; }

        /// <summary>Gets or sets the required output shape, for services with a native structured-output mode.</summary>
        public string OutputSchema { get; set; }
    }
}
