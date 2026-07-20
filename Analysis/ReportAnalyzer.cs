using System;
using System.Threading;
using System.Threading.Tasks;
using Backtester.Report;

namespace Backtester.Analysis
{
    /// <summary>
    /// Turns a run into an Analysis: it reduces the report model to an Analysis digest, asks an Analysis
    /// client, and owns the whole contract, so an Analysis reads the same whichever AI produced it. It is
    /// AI-agnostic and makes no outbound call itself.
    /// </summary>
    public class ReportAnalyzer
    {
        private readonly IAnalysisClient _client;
        private readonly AnalysisOptions _options;
        private readonly AnalysisDigestBuilder _builder = new();
        private readonly AnalysisDigestRenderer _renderer = new();

        /// <summary>Creates an analyzer that asks the supplied client, configured by the supplied options.</summary>
        public ReportAnalyzer(IAnalysisClient client, AnalysisOptions options)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <summary>
        /// Builds the Analysis digest for the supplied model, asks the client, and returns its raw answer.
        /// </summary>
        public Task<string> AnalyzeAsync(ReportModel model, CancellationToken cancellationToken)
        {
            if (model == null)
            {
                throw new ArgumentNullException(nameof(model));
            }

            AnalysisDigest digest = _builder.Build(model, _options);
            AnalysisRequest request = new() { UserPrompt = _renderer.Render(digest) + BuildGuidance() };
            return _client.AskAsync(request, cancellationToken);
        }

        /// <summary>
        /// Renders the caller's guidance as its own section, or nothing when no guidance was supplied.
        /// </summary>
        private string BuildGuidance()
        {
            if (string.IsNullOrWhiteSpace(_options.Guidance))
            {
                return string.Empty;
            }

            return "## Guidance" + Environment.NewLine + _options.Guidance + Environment.NewLine;
        }
    }
}
