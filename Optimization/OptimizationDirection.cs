namespace Backtester.Optimization
{
    /// <summary>
    /// The direction an <see cref="Objective"/> ranks Trials in: whether a higher or a lower Score wins.
    /// </summary>
    public enum OptimizationDirection
    {
        /// <summary>A higher Score is better; Trials are ranked from highest to lowest.</summary>
        Maximize,

        /// <summary>A lower Score is better; Trials are ranked from lowest to highest.</summary>
        Minimize
    }
}
