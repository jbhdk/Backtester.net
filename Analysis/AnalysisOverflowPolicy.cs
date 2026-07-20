namespace Backtester.Analysis
{
    /// <summary>
    /// What the analyzer does with a run carrying more round trips than the Analysis digest admits.
    /// </summary>
    public enum AnalysisOverflowPolicy
    {
        /// <summary>Reject the run with an <see cref="AnalysisDigestOverflowException"/>. The default.</summary>
        Throw = 0,

        /// <summary>
        /// Analyse an evenly spaced sample of the round trips spanning the whole run. The digest declares
        /// its own sampling, so the AI is told it is reasoning over part of a run.
        /// </summary>
        Sample = 1
    }
}
