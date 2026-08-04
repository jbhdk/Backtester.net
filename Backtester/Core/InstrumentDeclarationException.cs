using System;

namespace Backtester.Core
{
    /// <summary>
    /// Thrown when an Instrument cannot be declared. Either its currency declaration is internally
    /// inconsistent with the Account currency — no quote currency at all, a quote currency differing from
    /// the Account currency with no Conversion symbol, or a quote currency equal to the Account currency
    /// that nevertheless declares one — or a Provider's Instrument factory was handed a symbol it cannot
    /// read as one of that provider's pairs. Either way the Instrument fails at declaration time rather
    /// than mid-run: the cross-check runs when the Currency converter is built, and a factory rejects
    /// before it returns anything at all.
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
