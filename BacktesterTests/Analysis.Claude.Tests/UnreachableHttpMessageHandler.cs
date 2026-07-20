using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace BacktesterTests.Analysis.Claude.Tests
{
    /// <summary>
    /// A handler that fails every request the way an unreachable endpoint does — the request never
    /// arrives, so there is no status to report.
    /// </summary>
    public class UnreachableHttpMessageHandler : HttpMessageHandler
    {
        /// <summary>Fails as though the endpoint could not be contacted.</summary>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new HttpRequestException("No such host is known.");
        }
    }
}
