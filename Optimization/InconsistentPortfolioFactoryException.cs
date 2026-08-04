using System;

namespace Backtester.Optimization
{
    /// <summary>
    /// Thrown when a Trial's Portfolio declares a Conversion symbol the sweep never pre-fetched. The
    /// Optimizer reads the series to fetch from one Portfolio built at setup, which a
    /// <see cref="Func{Portfolio}"/> cannot promise every later call will match. A Trial declaring a symbol
    /// outside that set would read an empty series and fail deep inside a bar loop with a missing-rate error
    /// blaming the data, so the sweep is refused here instead: the fault is an inconsistent portfolio
    /// factory, and it ends the sweep rather than becoming a Rejected trial (ADR 0027).
    /// </summary>
    public class InconsistentPortfolioFactoryException : Exception
    {
        /// <summary>The Conversion symbol a Trial's Portfolio declared that the sweep did not pre-fetch.</summary>
        public string ConversionSymbol { get; }

        /// <summary>
        /// Initializes a new exception naming <paramref name="conversionSymbol"/> and the portfolio factory
        /// as the cause.
        /// </summary>
        public InconsistentPortfolioFactoryException(string conversionSymbol)
            : base(BuildMessage(conversionSymbol))
        {
            ConversionSymbol = conversionSymbol;
        }

        private static string BuildMessage(string conversionSymbol)
        {
            return $"A Trial's Portfolio declares Conversion symbol {conversionSymbol}, which this sweep did " +
                $"not pre-fetch. The portfolio factory returned different Instruments than the Portfolio it " +
                "built at setup; every call to the portfolio factory must declare the same Instruments.";
        }
    }
}
