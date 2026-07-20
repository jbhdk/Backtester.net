using System;
using Backtester.Report;

namespace Backtester.Analysis
{
    /// <summary>
    /// One row of the digest's Performance table: the label the report shows for a statistic, paired
    /// with how that statistic is read off a <see cref="ReportStats"/> and formatted.
    /// </summary>
    internal class DigestStatsMetric
    {
        private readonly Func<ReportStats, string> _value;

        /// <summary>Creates a metric with the supplied report label and value selector.</summary>
        public DigestStatsMetric(string label, Func<ReportStats, string> value)
        {
            Label = label;
            _value = value;
        }

        /// <summary>Gets the label the report shows for this statistic.</summary>
        public string Label { get; }

        /// <summary>Reads and formats this statistic from the supplied stats.</summary>
        public string ValueOf(ReportStats stats)
        {
            return _value(stats);
        }
    }
}
