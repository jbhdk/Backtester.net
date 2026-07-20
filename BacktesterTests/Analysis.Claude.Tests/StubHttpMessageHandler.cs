using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace BacktesterTests.Analysis.Claude.Tests
{
    /// <summary>
    /// A handler that answers every request with a canned response and keeps the request it was given,
    /// so a test can assert on what the client actually sent without a key or a network.
    /// </summary>
    public class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _responseBody;

        /// <summary>Creates a handler answering with the supplied status and body.</summary>
        public StubHttpMessageHandler(HttpStatusCode statusCode, string responseBody)
        {
            _statusCode = statusCode;
            _responseBody = responseBody;
        }

        /// <summary>Gets the number of requests this handler was asked to send.</summary>
        public int RequestCount { get; private set; }

        /// <summary>Gets the body of the last request, or null when none was sent.</summary>
        public string LastRequestBody { get; private set; }

        /// <summary>Records the request and answers with the canned response.</summary>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            if (request.Content != null)
            {
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            return new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseBody, System.Text.Encoding.UTF8, "application/json")
            };
        }
    }
}
