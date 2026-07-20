using System.Collections.Generic;
using Backtester.Report;

namespace Backtester.Analysis
{
    /// <summary>
    /// The deliberately reduced view of a run handed to an Analyzer: the run context, the Performance
    /// stats, the per-symbol stats, the round trips, the rejected orders, and the caller's configuration.
    /// Candles, indicator series, and the raw equity curve are for the reader's eye and are left out.
    /// A digest whose round trips were sampled says so, so the Analysis is never mistaken for a whole-run
    /// conclusion.
    /// </summary>
    internal class AnalysisDigest
    {
        /// <summary>Gets or sets the run context.</summary>
        public ReportRunInfo Run { get; set; }

        /// <summary>Gets or sets the caller's configuration cards.</summary>
        public IReadOnlyList<ReportCard> Configuration { get; set; }

        /// <summary>Gets or sets the Performance stats for all symbols combined.</summary>
        public ReportStats Stats { get; set; }

        // Key: symbol/ticker -> that symbol's standalone Performance stats.
        /// <summary>Gets or sets the per-symbol Performance stats, keyed by symbol.</summary>
        public IReadOnlyDictionary<string, ReportStats> StatsBySymbol { get; set; }

        /// <summary>Gets or sets the round trips the digest carries — the whole run, or the sample of it.</summary>
        public IReadOnlyList<ReportRoundTrip> RoundTrips { get; set; }

        /// <summary>Gets or sets the orders the broker declined.</summary>
        public IReadOnlyList<ReportRejectedOrder> RejectedOrders { get; set; }

        /// <summary>Gets or sets the number of round trips the run actually has, sampled or not.</summary>
        public int TotalRoundTrips { get; set; }

        /// <summary>Gets or sets whether <see cref="RoundTrips"/> is a sample rather than the whole run.</summary>
        public bool IsSampled { get; set; }

        /// <summary>Gets or sets how the sample was selected, stated in the digest when sampled.</summary>
        public string SelectionBasis { get; set; }
    }
}
