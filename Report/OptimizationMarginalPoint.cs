namespace Backtester.Report
{
    /// <summary>
    /// One point of a Parameter's marginal view: a single value of that Parameter with the Score summarised
    /// over every Trial that shares the value (the other varying Parameters differ across those Trials).
    /// Carries both the best achievable Score and the mean, so the renderer can show the peak against the
    /// typical behaviour along the axis. A pure display DTO.
    /// </summary>
    public class OptimizationMarginalPoint
    {
        /// <summary>Gets or sets this Parameter value as a display string.</summary>
        public string Value { get; set; }

        /// <summary>Gets or sets the highest Score among the Trials that share this value.</summary>
        public decimal MaxScore { get; set; }

        /// <summary>Gets or sets the mean Score among the Trials that share this value.</summary>
        public decimal MeanScore { get; set; }
    }
}
