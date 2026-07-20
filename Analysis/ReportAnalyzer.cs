using System;
using System.Text.Json;
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
        private readonly AnalysisResponseMapper _mapper = new();
        private readonly AnalysisResponseValidator _validator = new();

        /// <summary>Creates an analyzer that asks the supplied client, configured by the supplied options.</summary>
        public ReportAnalyzer(IAnalysisClient client, AnalysisOptions options)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <summary>
        /// Builds the Analysis digest for the supplied model, asks the client, and maps its answer onto
        /// an Analysis. The AI is an untrusted source, so the answer is validated strictly and never
        /// repaired: a violation costs one retry carrying the validation error, and a second violation
        /// throws (ADR 0019).
        /// </summary>
        public async Task<ReportAnalysis> AnalyzeAsync(ReportModel model, CancellationToken cancellationToken)
        {
            if (model == null)
            {
                throw new ArgumentNullException(nameof(model));
            }

            AnalysisDigest digest = _builder.Build(model, _options);
            string answer = await _client.AskAsync(BuildRequest(digest, null), cancellationToken);
            AnalysisResponse response = Interpret(answer, out string violation);
            if (violation != null)
            {
                answer = await _client.AskAsync(BuildRequest(digest, violation), cancellationToken);
                response = Interpret(answer, out violation);
            }

            if (violation != null)
            {
                throw new AnalysisFormatException(violation);
            }

            return _mapper.Map(response, BuildProvenance());
        }

        /// <summary>
        /// Reads the supplied answer as an Analysis response, reporting the first violation of the
        /// contract it commits — being ill-formed included — or null when it satisfies the contract.
        /// </summary>
        private AnalysisResponse Interpret(string answer, out string violation)
        {
            AnalysisResponse response;
            try
            {
                response = JsonSerializer.Deserialize<AnalysisResponse>(answer);
            }
            catch (JsonException exception)
            {
                violation = "The answer is not well-formed JSON: " + exception.Message;
                return null;
            }

            violation = _validator.Validate(response);
            return response;
        }

        /// <summary>
        /// Assembles the request for the supplied digest, appending a correction naming the supplied
        /// violation when this is the retry. The built-in instructions and the contract's schema are the
        /// Analyzer's own, so every service is asked the same question in the same shape; only the digest
        /// and the caller's guidance vary from run to run.
        /// </summary>
        private AnalysisRequest BuildRequest(AnalysisDigest digest, string violation)
        {
            return new AnalysisRequest
            {
                SystemPrompt = AnalysisSystemPrompt.Text,
                UserPrompt = _renderer.Render(digest) + BuildGuidance() + BuildCorrection(violation),
                OutputSchema = AnalysisSchema.Json
            };
        }

        /// <summary>
        /// Renders the violation the previous answer committed as its own section, so the model can
        /// correct itself, or nothing when this is the first attempt.
        /// </summary>
        private static string BuildCorrection(string violation)
        {
            if (violation == null)
            {
                return string.Empty;
            }

            return "## Correction" + Environment.NewLine + Environment.NewLine +
                "Your previous answer did not satisfy the contract:" + Environment.NewLine +
                violation + Environment.NewLine + Environment.NewLine +
                "Answer again, satisfying the schema exactly." + Environment.NewLine;
        }

        /// <summary>
        /// Records what produced the Analysis, so the rendered section is unmistakably machine-generated
        /// and a reader can tell a small model's critique from a frontier model's (ADR 0019).
        /// </summary>
        private AnalysisProvenance BuildProvenance()
        {
            return new AnalysisProvenance
            {
                Service = _client.ServiceName,
                Model = _options.ModelName,
                GeneratedAtUtc = DateTime.UtcNow
            };
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
