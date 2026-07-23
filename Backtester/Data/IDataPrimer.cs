using System;
using System.Threading;
using System.Threading.Tasks;

namespace Backtester.Data
{
    /// <summary>
    /// Warms the Cache for a range of bars ahead of any backtest, so later runs over sub-ranges are
    /// served entirely from the Cache without contacting the Provider. Separate from
    /// <see cref="IHistoricalDataFetcher"/> so the engine's fetch seam is unaffected (ISP).
    /// </summary>
    public interface IDataPrimer
    {
        /// <summary>
        /// Fetches the full range for each symbol from the Provider, merges it into the Cache, and lowers each
        /// symbol's Coverage floor to <paramref name="fromUtc"/>. Symbols are primed concurrently.
        /// </summary>
        Task PrimeAsync(string[] symbols, DateTime fromUtc, DateTime toUtc, string interval, CancellationToken ct = default);
    }
}
