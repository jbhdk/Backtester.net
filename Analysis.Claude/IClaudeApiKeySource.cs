namespace Backtester.Analysis.Claude
{
    /// <summary>
    /// Supplies the credential the Claude Analysis client authenticates with. It exists so the key is
    /// never a constructor argument the caller has to carry: the client asks for it, and in production
    /// the answer comes from the environment.
    /// </summary>
    public interface IClaudeApiKeySource
    {
        /// <summary>Reads the API key, or null when none is configured.</summary>
        string Read();
    }
}
