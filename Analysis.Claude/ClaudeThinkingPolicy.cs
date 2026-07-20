using System.Globalization;
using System.Text.RegularExpressions;
using Anthropic.Models.Messages;

namespace Backtester.Analysis.Claude
{
    /// <summary>
    /// Chooses the thinking configuration for a model, because the Claude family is not uniform about
    /// it: adaptive thinking arrived with 4.6, and models older than that reject it and require an
    /// explicit budget instead. Deriving the choice from the configured model is what lets one client
    /// serve the whole range without a code change.
    /// </summary>
    internal static class ClaudeThinkingPolicy
    {
        /// <summary>The first model version that accepts adaptive thinking.</summary>
        private const decimal FirstAdaptiveVersion = 4.6m;

        /// <summary>
        /// Reads the version out of a model name, matching both the two-part form
        /// (<c>claude-opus-4-8</c>) and the single-major form (<c>claude-sonnet-5</c>).
        /// </summary>
        private static readonly Regex VersionPattern = new(@"-(\d+)(?:-(\d+))?(?:-|$)", RegexOptions.Compiled);

        /// <summary>
        /// Returns the thinking configuration the supplied model accepts: adaptive for 4.6 and later,
        /// an explicit budget below <paramref name="maxTokens"/> for anything older. A model whose
        /// version cannot be read is treated as current, since adaptive is the form the family is
        /// moving to.
        /// </summary>
        public static ThinkingConfigParam For(string modelName, int maxTokens)
        {
            if (ReadVersion(modelName) < FirstAdaptiveVersion)
            {
                return new ThinkingConfigEnabled { BudgetTokens = maxTokens / 2 };
            }

            return new ThinkingConfigAdaptive();
        }

        /// <summary>
        /// Reads the model's version, or <see cref="decimal.MaxValue"/> when the name does not carry
        /// one in a form this understands.
        /// </summary>
        private static decimal ReadVersion(string modelName)
        {
            Match match = VersionPattern.Match(modelName);
            if (!match.Success)
            {
                return decimal.MaxValue;
            }

            string version = match.Groups[2].Success
                ? match.Groups[1].Value + "." + match.Groups[2].Value
                : match.Groups[1].Value;

            return decimal.Parse(version, CultureInfo.InvariantCulture);
        }
    }
}
