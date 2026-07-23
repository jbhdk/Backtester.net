using System;

namespace Backtester.Data
{
    /// <summary>
    /// Thrown when a run requests bars starting before the Cache's Coverage floor — the earliest range
    /// start ever asked of the Provider. Below the floor the Cache's lack of bars is unknown (that window
    /// was never requested), so the Fetcher refuses the run rather than serve a silently short slice.
    /// </summary>
    public class DataCoverageException : Exception
    {
        /// <summary>The symbol whose Cache does not cover the requested range.</summary>
        public string Symbol { get; }

        /// <summary>The requested range start that falls before the Coverage floor.</summary>
        public DateTime RequestedFromUtc { get; }

        /// <summary>The Coverage floor: the earliest range start ever asked of the Provider.</summary>
        public DateTime CoverageFloorUtc { get; }

        /// <summary>The bar interval of the requested range.</summary>
        public string Interval { get; }

        /// <summary>
        /// Initializes a new exception describing the uncovered range and the remedy.
        /// </summary>
        public DataCoverageException(string symbol, DateTime requestedFromUtc, DateTime coverageFloorUtc, string interval)
            : base(BuildMessage(symbol, requestedFromUtc, coverageFloorUtc, interval))
        {
            Symbol = symbol;
            RequestedFromUtc = requestedFromUtc;
            CoverageFloorUtc = coverageFloorUtc;
            Interval = interval;
        }

        private static string BuildMessage(string symbol, DateTime requestedFromUtc, DateTime coverageFloorUtc, string interval)
        {
            return $"{symbol} {interval}: requested range starts {requestedFromUtc:yyyy-MM-dd} but data has only ever been " +
                $"fetched from {coverageFloorUtc:yyyy-MM-dd}. Delete the cache file {symbol}_{interval}.csv to re-fetch from the earlier start.";
        }
    }
}
