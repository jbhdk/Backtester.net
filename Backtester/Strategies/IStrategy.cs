using System.Collections.Generic;
using Backtester.Broker;
using Backtester.Core;

namespace Backtester.Strategies
{
    /// <summary>
    /// Defines the contract for a trading strategy that reacts to market bars.
    /// </summary>
    public interface IStrategy
    {
        /// <summary>
        /// Called once before the first bar with the complete per-symbol bar history from the feed.
        /// Strategies use this to pre-compute indicators with any external library.
        /// </summary>
        void OnStart(IReadOnlyDictionary<string, IReadOnlyList<Candle>> history);

        /// <summary>
        /// Called on each bar for the given symbol. Strategies submit orders directly via <paramref name="broker"/>.
        /// </summary>
        void OnBar(string symbol, Candle bar, PortfolioSnapshot snapshot, IBroker broker);
    }
}
