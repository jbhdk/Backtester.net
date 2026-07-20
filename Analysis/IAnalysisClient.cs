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
        /// <summary>Asks the service for an answer to the supplied request and returns its raw response text.</summary>
        Task<string> AskAsync(AnalysisRequest request, CancellationToken cancellationToken);
    }
}
