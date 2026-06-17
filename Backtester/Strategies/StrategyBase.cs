using System.Collections.Generic;
using Backtester.Broker;
using Backtester.Core;

namespace Backtester.Strategies
{
    /// <summary>
    /// Base class for all strategies, providing a default <see cref="IStrategy"/> implementation scaffold.
    /// </summary>
    public abstract class StrategyBase : IStrategy
    {
        /// <summary>
        /// Called once before the first bar with the complete per-symbol bar history from the feed.
        /// Override to pre-compute indicators; the default implementation is a no-op.
        /// </summary>
        public virtual void OnStart(IReadOnlyDictionary<string, IReadOnlyList<Candle>> history) { }

        /// <summary>
        /// Called on each bar for the given symbol. Strategies submit orders directly via <paramref name="broker"/>.
        /// </summary>
        public abstract void OnBar(string symbol, Candle bar, PortfolioSnapshot snapshot, IBroker broker);
    }
}
