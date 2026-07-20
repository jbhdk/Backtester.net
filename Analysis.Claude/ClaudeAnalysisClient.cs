using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;

namespace Backtester.Analysis.Claude
{
    /// <summary>
    /// The Analysis client for Anthropic's Claude. It carries an <see cref="AnalysisRequest"/> to the
    /// service and returns the raw answer, interpreting nothing: the Analyzer owns the contract, so an
    /// Analysis reads the same whichever AI produced it. Deliberately not called a Provider — a
    /// Provider fetches bars.
    /// </summary>
    public class ClaudeAnalysisClient : IAnalysisClient
    {
        /// <summary>
        /// The output ceiling for one answer. An Analysis is a summary and a handful of Findings, so
        /// this is generous rather than tuned; it also bounds the thinking budget on models that need
        /// one stated explicitly.
        /// </summary>
        private const int MaxTokens = 16000;

        private readonly string _modelName;
        private readonly HttpClient _httpClient;
        private readonly IClaudeApiKeySource _apiKeySource;

        /// <summary>
        /// Creates a client asking the supplied model. The <paramref name="httpClient"/> and
        /// <paramref name="apiKeySource"/> are seams: left null, the SDK's own transport and the
        /// <c>ANTHROPIC_API_KEY</c> environment variable are used.
        /// </summary>
        public ClaudeAnalysisClient(string modelName, HttpClient httpClient = null, IClaudeApiKeySource apiKeySource = null)
        {
            if (string.IsNullOrWhiteSpace(modelName))
            {
                throw new ArgumentException("A model name is required.", nameof(modelName));
            }

            _modelName = modelName;
            _httpClient = httpClient;
            _apiKeySource = apiKeySource ?? new EnvironmentClaudeApiKeySource();
        }

        /// <summary>Gets the name of the service this client talks to, recorded in the Analysis's Provenance.</summary>
        public string ServiceName => "Claude";

        /// <summary>Asks Claude for an answer to the supplied request and returns its raw response text.</summary>
        public async Task<string> AskAsync(AnalysisRequest request, CancellationToken cancellationToken)
        {
            AnthropicClient client = new()
            {
                ApiKey = ReadApiKey(),
                HttpClient = _httpClient
            };

            MessageCreateParams parameters = new()
            {
                Model = _modelName,
                MaxTokens = MaxTokens,
                System = request.SystemPrompt,
                Thinking = ClaudeThinkingPolicy.For(_modelName, MaxTokens),
                OutputConfig = BuildOutputConfig(request.OutputSchema),
                Messages = [new MessageParam { Role = Role.User, Content = request.UserPrompt }]
            };

            Message message = await SendAsync(client, parameters, cancellationToken);
            return ReadText(message);
        }

        /// <summary>
        /// Sends the request, translating a rejection by the service into a failure that names it. An
        /// answered-but-refused request is a different problem from one that never arrived, so the two
        /// are never reported the same way.
        /// </summary>
        private static async Task<Message> SendAsync(AnthropicClient client, MessageCreateParams parameters, CancellationToken cancellationToken)
        {
            try
            {
                return await client.Messages.Create(parameters, cancellationToken);
            }
            catch (AnthropicApiException exception)
            {
                throw new ClaudeAnalysisException(
                    $"Claude rejected the request (HTTP {(int)exception.StatusCode}): {exception.Message}", exception);
            }
            catch (AnthropicIOException exception)
            {
                throw new ClaudeAnalysisException(
                    $"Claude could not be reached: {exception.InnerException?.Message ?? exception.Message}", exception);
            }
        }

        /// <summary>
        /// Reads the credential, failing before anything is sent when none is configured. The message
        /// names the variable and where to create a key, so a missing credential is never mistaken for
        /// the service rejecting one.
        /// </summary>
        private string ReadApiKey()
        {
            string apiKey = _apiKeySource.Read();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new ClaudeAnalysisException(
                    $"No Claude credential is configured: the {EnvironmentClaudeApiKeySource.VariableName} " +
                    "environment variable is not set. Create a key at https://console.anthropic.com/settings/keys.");
            }

            return apiKey;
        }

        /// <summary>
        /// Hands the contract's schema to Claude's structured-output mode, so the shape constrains
        /// generation rather than being requested in prose and repaired afterwards. Returns null when
        /// the request carries no schema, which leaves the answer unconstrained.
        /// </summary>
        private static OutputConfig BuildOutputConfig(string outputSchema)
        {
            if (string.IsNullOrWhiteSpace(outputSchema))
            {
                return null;
            }

            return new OutputConfig
            {
                Format = new JsonOutputFormat
                {
                    Schema = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(outputSchema)
                }
            };
        }

        /// <summary>
        /// Concatenates the answer's text blocks, returning it exactly as the service wrote it. The
        /// client interprets nothing: reading the answer is the Analyzer's job.
        /// </summary>
        private static string ReadText(Message message)
        {
            StringBuilder text = new();
            foreach (TextBlock block in message.Content.Select(block => block.Value).OfType<TextBlock>())
            {
                text.Append(block.Text);
            }

            return text.ToString();
        }
    }
}
