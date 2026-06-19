using System.Threading;
using System.Threading.Tasks;

namespace Backtester.Engine
{
    /// <summary>
    /// Controls the execution lifecycle of a backtest run.
    /// </summary>
    public interface IEngine
    {
        /// <summary>
        /// Fetches market data for the configured symbols, then processes bars in a loop until the data is
        /// exhausted or <see cref="Stop"/> is called, returning the run's outputs as a <see cref="BacktestResult"/>.
        /// </summary>
        Task<BacktestResult> StartAsync(CancellationToken ct = default);

        /// <summary>Signals the engine to halt after completing the current bar.</summary>
        void Stop();
    }
}
