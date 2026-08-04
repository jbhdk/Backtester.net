using System;

namespace Backtester.Core
{
    /// <summary>
    /// Thrown when an Instrument's currency declaration is internally inconsistent with the Account
    /// currency: no quote currency at all, a quote currency differing from the Account currency with no
    /// Conversion symbol, or a quote currency equal to the Account currency that nevertheless declares one.
    /// The cross-check runs when the Currency converter is built, so a mis-declared Instrument fails the
    /// run at construction rather than mid-way through it.
    /// </summary>
    public class InstrumentDeclarationException : Exception
    {
        /// <summary>The symbol whose declaration is inconsistent.</summary>
        public string Symbol { get; }

        /// <summary>
        /// Initializes a new exception naming <paramref name="symbol"/> and describing how its declaration
        /// is inconsistent.
        /// </summary>
        public InstrumentDeclarationException(string symbol, string problem)
            : base($"Instrument {symbol}: {problem}")
        {
            Symbol = symbol;
        }
    }
}
