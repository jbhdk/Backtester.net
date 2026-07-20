namespace Backtester.Report
{
    /// <summary>
    /// How much a Finding matters. A Finding may also be a <see cref="Strength"/> — something the run
    /// does well — which is not a low-severity problem and renders visually distinct from one.
    /// </summary>
    public enum FindingSeverity
    {
        /// <summary>A problem that undermines confidence in the run's result.</summary>
        High,

        /// <summary>A problem worth addressing before the strategy is trusted further.</summary>
        Medium,

        /// <summary>A minor problem or a refinement.</summary>
        Low,

        /// <summary>Something the run does well. Not a problem at all.</summary>
        Strength
    }
}
