using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Backtester.Analysis;
using Backtester.Analysis.Claude;
using FakeItEasy;
using Xunit;

namespace BacktesterTests.Analysis.Claude.Tests
{
    /// <summary>
    /// Covers <see cref="ClaudeAnalysisClient"/> through its public entry point, asserting what it sends
    /// by capturing the request handed to a stub transport. No test needs a key or a network.
    /// </summary>
    public class ClaudeAnalysisClientTests
    {
        [Fact]
        public async Task AskAsync_returns_the_services_answer_unaltered()
        {
            StubHttpMessageHandler handler = new StubHttpMessageHandler(
                HttpStatusCode.OK,
                BuildResponse("{\"summary\":\"Fine.\",\"findings\":[]}"));
            ClaudeAnalysisClient client = BuildClient(handler);

            string answer = await client.AskAsync(BuildRequest(), CancellationToken.None);

            Assert.Equal("{\"summary\":\"Fine.\",\"findings\":[]}", answer);
        }

        [Fact]
        public void ServiceName_is_the_service_the_client_talks_to()
        {
            ClaudeAnalysisClient client = BuildClient(new StubHttpMessageHandler(HttpStatusCode.OK, string.Empty));

            string serviceName = client.ServiceName;

            Assert.Equal("Claude", serviceName);
        }

        [Fact]
        public async Task AskAsync_sends_the_requests_system_prompt_as_the_instructions()
        {
            StubHttpMessageHandler handler = new StubHttpMessageHandler(HttpStatusCode.OK, BuildResponse("{}"));
            ClaudeAnalysisClient client = BuildClient(handler);

            await client.AskAsync(BuildRequest(), CancellationToken.None);

            JsonDocument body = JsonDocument.Parse(handler.LastRequestBody);
            Assert.Contains("You are an analyst.", body.RootElement.GetProperty("system").ToString());
        }

        [Fact]
        public async Task AskAsync_sends_the_requests_user_prompt_as_the_user_message()
        {
            StubHttpMessageHandler handler = new StubHttpMessageHandler(HttpStatusCode.OK, BuildResponse("{}"));
            ClaudeAnalysisClient client = BuildClient(handler);

            await client.AskAsync(BuildRequest(), CancellationToken.None);

            JsonDocument body = JsonDocument.Parse(handler.LastRequestBody);
            Assert.Contains("Some digest.", body.RootElement.GetProperty("messages").ToString());
        }

        [Fact]
        public async Task AskAsync_sends_the_configured_model_name()
        {
            StubHttpMessageHandler handler = new StubHttpMessageHandler(HttpStatusCode.OK, BuildResponse("{}"));
            ClaudeAnalysisClient client = BuildClient(handler, "claude-haiku-4-5");

            await client.AskAsync(BuildRequest(), CancellationToken.None);

            JsonDocument body = JsonDocument.Parse(handler.LastRequestBody);
            Assert.Equal("claude-haiku-4-5", body.RootElement.GetProperty("model").GetString());
        }

        [Fact]
        public async Task AskAsync_sends_the_contracts_schema_as_a_structured_output()
        {
            StubHttpMessageHandler handler = new StubHttpMessageHandler(HttpStatusCode.OK, BuildResponse("{}"));
            ClaudeAnalysisClient client = BuildClient(handler);

            await client.AskAsync(BuildRequest(), CancellationToken.None);

            JsonDocument body = JsonDocument.Parse(handler.LastRequestBody);
            JsonElement format = body.RootElement.GetProperty("output_config").GetProperty("format");
            Assert.Equal("json_schema", format.GetProperty("type").GetString());
            Assert.False(format.GetProperty("schema").GetProperty("additionalProperties").GetBoolean());
        }

        [Fact]
        public async Task AskAsync_does_not_ask_for_the_schema_in_prose()
        {
            StubHttpMessageHandler handler = new StubHttpMessageHandler(HttpStatusCode.OK, BuildResponse("{}"));
            ClaudeAnalysisClient client = BuildClient(handler);

            await client.AskAsync(BuildRequest(), CancellationToken.None);

            JsonDocument body = JsonDocument.Parse(handler.LastRequestBody);
            Assert.DoesNotContain("additionalProperties", body.RootElement.GetProperty("messages").ToString());
            Assert.DoesNotContain("additionalProperties", body.RootElement.GetProperty("system").ToString());
        }

        [Fact]
        public async Task AskAsync_names_the_environment_variable_when_the_credential_is_missing()
        {
            StubHttpMessageHandler handler = new StubHttpMessageHandler(HttpStatusCode.OK, BuildResponse("{}"));
            ClaudeAnalysisClient client = BuildClientWithoutCredential(handler);

            ClaudeAnalysisException failure = await Assert.ThrowsAsync<ClaudeAnalysisException>(
                () => client.AskAsync(BuildRequest(), CancellationToken.None));

            Assert.Contains("ANTHROPIC_API_KEY", failure.Message);
        }

        [Fact]
        public async Task AskAsync_does_not_call_the_service_when_the_credential_is_missing()
        {
            StubHttpMessageHandler handler = new StubHttpMessageHandler(HttpStatusCode.OK, BuildResponse("{}"));
            ClaudeAnalysisClient client = BuildClientWithoutCredential(handler);

            await Assert.ThrowsAsync<ClaudeAnalysisException>(
                () => client.AskAsync(BuildRequest(), CancellationToken.None));

            Assert.Equal(0, handler.RequestCount);
        }

        [Fact]
        public async Task AskAsync_names_the_service_and_the_status_when_the_service_rejects_the_request()
        {
            StubHttpMessageHandler handler = new StubHttpMessageHandler(
                HttpStatusCode.BadRequest,
                "{\"type\":\"error\",\"error\":{\"type\":\"invalid_request_error\",\"message\":\"bad schema\"}}");
            ClaudeAnalysisClient client = BuildClient(handler);

            ClaudeAnalysisException failure = await Assert.ThrowsAsync<ClaudeAnalysisException>(
                () => client.AskAsync(BuildRequest(), CancellationToken.None));

            Assert.Contains("Claude", failure.Message);
            Assert.Contains("400", failure.Message);
        }

        [Fact]
        public async Task AskAsync_reports_an_unreachable_service_as_something_other_than_a_rejection()
        {
            ClaudeAnalysisClient client = BuildClient(new UnreachableHttpMessageHandler());

            ClaudeAnalysisException failure = await Assert.ThrowsAsync<ClaudeAnalysisException>(
                () => client.AskAsync(BuildRequest(), CancellationToken.None));

            Assert.Contains("could not be reached", failure.Message);
            Assert.DoesNotContain("rejected", failure.Message);
        }

        [Fact]
        public async Task AskAsync_asks_an_adaptive_capable_model_to_think_adaptively()
        {
            StubHttpMessageHandler handler = new StubHttpMessageHandler(HttpStatusCode.OK, BuildResponse("{}"));
            ClaudeAnalysisClient client = BuildClient(handler, "claude-opus-4-8");

            await client.AskAsync(BuildRequest(), CancellationToken.None);

            JsonDocument body = JsonDocument.Parse(handler.LastRequestBody);
            JsonElement thinking = body.RootElement.GetProperty("thinking");
            Assert.Equal("adaptive", thinking.GetProperty("type").GetString());
        }

        [Fact]
        public async Task AskAsync_gives_an_older_model_an_explicit_thinking_budget()
        {
            StubHttpMessageHandler handler = new StubHttpMessageHandler(HttpStatusCode.OK, BuildResponse("{}"));
            ClaudeAnalysisClient client = BuildClient(handler, "claude-haiku-4-5");

            await client.AskAsync(BuildRequest(), CancellationToken.None);

            JsonDocument body = JsonDocument.Parse(handler.LastRequestBody);
            JsonElement thinking = body.RootElement.GetProperty("thinking");
            Assert.Equal("enabled", thinking.GetProperty("type").GetString());
            Assert.InRange(thinking.GetProperty("budget_tokens").GetInt32(), 1024, body.RootElement.GetProperty("max_tokens").GetInt32() - 1);
        }

        /// <summary>Builds a client whose credential source finds nothing configured.</summary>
        private static ClaudeAnalysisClient BuildClientWithoutCredential(HttpMessageHandler handler)
        {
            IClaudeApiKeySource apiKeySource = A.Fake<IClaudeApiKeySource>();
            A.CallTo(() => apiKeySource.Read()).Returns(null);
            return new ClaudeAnalysisClient("claude-opus-4-8", new HttpClient(handler), apiKeySource);
        }

        /// <summary>Builds a client asking a stub transport with a credential that never leaves the test.</summary>
        private static ClaudeAnalysisClient BuildClient(HttpMessageHandler handler, string modelName = "claude-opus-4-8")
        {
            IClaudeApiKeySource apiKeySource = A.Fake<IClaudeApiKeySource>();
            A.CallTo(() => apiKeySource.Read()).Returns("test-key");
            return new ClaudeAnalysisClient(modelName, new HttpClient(handler), apiKeySource);
        }

        /// <summary>Builds a request carrying the three things an Analysis client is given.</summary>
        private static AnalysisRequest BuildRequest()
        {
            return new AnalysisRequest
            {
                SystemPrompt = "You are an analyst.",
                UserPrompt = "## Run\n\nSome digest.",
                OutputSchema = "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}"
            };
        }

        /// <summary>Wraps the supplied text in a Messages API response body.</summary>
        private static string BuildResponse(string text)
        {
            return "{\"id\":\"msg_test\",\"type\":\"message\",\"role\":\"assistant\"," +
                "\"model\":\"claude-opus-4-8\"," +
                "\"content\":[{\"type\":\"text\",\"text\":" + System.Text.Json.JsonSerializer.Serialize(text) + "}]," +
                "\"stop_reason\":\"end_turn\",\"stop_sequence\":null," +
                "\"usage\":{\"input_tokens\":1,\"output_tokens\":1}}";
        }
    }
}
