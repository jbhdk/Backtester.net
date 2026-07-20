using System;
using System.Collections.Generic;
using Backtester.Report;

namespace Backtester.Analysis
{
    /// <summary>
    /// Reduces a report model to an <see cref="AnalysisDigest"/>, enforcing the round-trip cap: over the
    /// cap the run is rejected, unless the caller opted in to sampling.
    /// </summary>
    internal class AnalysisDigestBuilder
    {
        /// <summary>The basis a sampled digest declares for how its round trips were chosen.</summary>
        private const string EvenlySpaced = "evenly spaced across the run";

        /// <summary>Builds the digest for the supplied model under the supplied options.</summary>
        public AnalysisDigest Build(ReportModel model, AnalysisOptions options)
        {
            IReadOnlyList<ReportRoundTrip> roundTrips = model.RoundTrips ?? new List<ReportRoundTrip>();
            bool overCap = roundTrips.Count > options.RoundTripCap;

            if (overCap && options.OverflowPolicy == AnalysisOverflowPolicy.Throw)
            {
                throw new AnalysisDigestOverflowException(roundTrips.Count, options.RoundTripCap);
            }

            return new AnalysisDigest
            {
                Run = model.Run,
                Configuration = model.Configuration,
                Stats = model.Stats,
                StatsBySymbol = model.StatsBySymbol,
                RoundTrips = overCap ? Sample(roundTrips, options.RoundTripCap) : roundTrips,
                RejectedOrders = model.RejectedOrders,
                TotalRoundTrips = roundTrips.Count,
                IsSampled = overCap,
                SelectionBasis = overCap ? EvenlySpaced : null
            };
        }

        /// <summary>
        /// Selects <paramref name="cap"/> round trips spread evenly across the run, keeping the first and
        /// the last so the sample spans the whole run rather than one stretch of it.
        /// </summary>
        private static IReadOnlyList<ReportRoundTrip> Sample(IReadOnlyList<ReportRoundTrip> roundTrips, int cap)
        {
            List<ReportRoundTrip> sample = new(cap);

            if (cap == 1)
            {
                sample.Add(roundTrips[0]);
                return sample;
            }

            for (int position = 0; position < cap; position++)
            {
                int index = (int)Math.Round(position * (roundTrips.Count - 1d) / (cap - 1d), MidpointRounding.AwayFromZero);
                sample.Add(roundTrips[index]);
            }

            return sample;
        }
    }
}
