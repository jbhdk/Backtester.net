using System;

namespace Backtester.Analysis.Claude
{
    /// <summary>
    /// Reads the Claude credential from the <c>ANTHROPIC_API_KEY</c> environment variable, which is
    /// where the client expects it in production.
    /// </summary>
    public class EnvironmentClaudeApiKeySource : IClaudeApiKeySource
    {
        /// <summary>The name of the environment variable the key is read from.</summary>
        public const string VariableName = "ANTHROPIC_API_KEY";

        /// <summary>Reads the API key from the environment, or null when the variable is not set.</summary>
        public string Read()
        {
            return Environment.GetEnvironmentVariable(VariableName);
        }
    }
}
