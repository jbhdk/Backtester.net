using System.Threading;
using System.Threading.Tasks;

namespace Backtester.Analysis
{
    /// <summary>
    /// The adapter for one AI service. It carries an <see cref="AnalysisRequest"/> to that service and
    /// returns the raw answer; it knows nothing about digests, Findings, or validity. Deliberately not
    /// called a Provider — a Provider fetches bars.
    /// </summary>
    public interface IAnalysisClient
    {
        /// <summary>
        /// Gets the name of the AI service this client talks to, e.g. <c>"Claude"</c>. The Analyzer
        /// records it in the Analysis's Provenance, so a reader can tell what produced a claim long
        /// after the run (ADR 0019). The client is the only thing that knows which service answered.
        /// </summary>
        string ServiceName { get; }

        /// <summary>Asks the service for an answer to the supplied request and returns its raw response text.</summary>
        Task<string> AskAsync(AnalysisRequest request, CancellationToken cancellationToken);
    }
}
