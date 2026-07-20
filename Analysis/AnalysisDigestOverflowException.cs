using System;
using System.Globalization;

namespace Backtester.Analysis
{
    /// <summary>
    /// Thrown when a run has more round trips than the Analysis digest admits. The run is rejected rather
    /// than silently reduced, so a critique is never mistaken for a whole-run conclusion; sampling is
    /// available when the caller asks for it explicitly.
    /// </summary>
    public class AnalysisDigestOverflowException : Exception
    {
        /// <summary>Creates the exception for a run of the supplied round-trip count against the supplied cap.</summary>
        public AnalysisDigestOverflowException(int roundTripCount, int roundTripCap)
            : base(string.Format(
                CultureInfo.InvariantCulture,
                "The run has {0} round trips, more than the Analysis digest's cap of {1}. Raise {2}.{3}, or enable sampling with {2}.{4} = {5}.{6} to analyse an evenly spaced sample of the run.",
                roundTripCount,
                roundTripCap,
                nameof(AnalysisOptions),
                nameof(AnalysisOptions.RoundTripCap),
                nameof(AnalysisOptions.OverflowPolicy),
                nameof(AnalysisOverflowPolicy),
                nameof(AnalysisOverflowPolicy.Sample)))
        {
            RoundTripCount = roundTripCount;
            RoundTripCap = roundTripCap;
        }

        /// <summary>Gets the number of round trips the run actually has.</summary>
        public int RoundTripCount { get; }

        /// <summary>Gets the cap the digest admits.</summary>
        public int RoundTripCap { get; }
    }
}
