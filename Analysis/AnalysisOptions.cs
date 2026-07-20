namespace Backtester.Analysis
{
    /// <summary>
    /// The caller's settings for one Analysis: which model to ask and what to tell it about the run.
    /// </summary>
    public class AnalysisOptions
    {
        /// <summary>Gets or sets the name of the model to ask, as that service names it (e.g. <c>"qwen2.5:14b"</c>).</summary>
        public string ModelName { get; set; }

        /// <summary>
        /// Gets or sets the optional free-text guidance appended to the digest: the strategy's intent or a
        /// focus for this run that the digest itself cannot express. It appends to the analyzer's own
        /// instructions and never replaces them.
        /// </summary>
        public string Guidance { get; set; }

        /// <summary>
        /// Gets or sets the most round trips the Analysis digest admits. Digest size is bounded by
        /// round-trip count rather than by a token estimate, so the bound is deterministic. Defaults to 500.
        /// </summary>
        public int RoundTripCap { get; set; } = 500;

        /// <summary>
        /// Gets or sets what happens to a run exceeding <see cref="RoundTripCap"/>. Defaults to
        /// <see cref="AnalysisOverflowPolicy.Throw"/>: sampling is opt-in.
        /// </summary>
        public AnalysisOverflowPolicy OverflowPolicy { get; set; } = AnalysisOverflowPolicy.Throw;
    }
}
