namespace Backtester.Report
{
    /// <summary>
    /// One observation about a run paired with the change it recommends. Observation and recommendation
    /// are separate members on purpose: evidence must be stated before a prescription is made.
    /// </summary>
    public class ReportFinding
    {
        /// <summary>Gets or sets the area of the run the Finding concerns. Serialized to the page as its name.</summary>
        public FindingCategory Category { get; set; }

        /// <summary>Gets or sets how much the Finding matters. Serialized to the page as its name.</summary>
        public FindingSeverity Severity { get; set; }

        /// <summary>Gets or sets the Finding's short headline.</summary>
        public string Title { get; set; }

        /// <summary>Gets or sets what the numbers show — the evidence the Finding rests on.</summary>
        public string Observation { get; set; }

        /// <summary>Gets or sets what to change in response to the observation.</summary>
        public string Recommendation { get; set; }
    }
}
