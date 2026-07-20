using System;

namespace Backtester.Analysis
{
    /// <summary>
    /// Thrown when an AI's answer violates the Analysis contract twice: once on its first attempt and
    /// again on the single retry it is given. The run gets a valid Analysis or no Analysis — a
    /// partially-understood one is never returned, because a reader cannot tell which parts the AI
    /// produced and which the code invented on its behalf (ADR 0019).
    /// </summary>
    public class AnalysisFormatException : Exception
    {
        /// <summary>Creates the exception for the supplied contract violation.</summary>
        public AnalysisFormatException(string violation)
            : base("The AI's answer violated the Analysis contract twice. " + violation)
        {
            Violation = violation;
        }

        /// <summary>Gets the violation the second answer committed, as fed back to the model.</summary>
        public string Violation { get; }
    }
}
