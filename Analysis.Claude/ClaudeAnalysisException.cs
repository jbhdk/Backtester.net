using System;

namespace Backtester.Analysis.Claude
{
    /// <summary>
    /// Thrown when an Analysis cannot be obtained from Claude. The message says which of the three
    /// things went wrong — the credential is missing, the service rejected the request, or the service
    /// could not be reached — because they are diagnosed differently and a bare SDK error confuses them.
    /// </summary>
    public class ClaudeAnalysisException : Exception
    {
        /// <summary>Creates an exception describing the failure.</summary>
        public ClaudeAnalysisException(string message) : base(message)
        {
        }

        /// <summary>Creates an exception describing the failure and the error that caused it.</summary>
        public ClaudeAnalysisException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
