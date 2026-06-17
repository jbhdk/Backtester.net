using System.Collections.Generic;
using Backtester.Core;

namespace Backtester.Strategies
{
    /// <summary>
    /// Base class for all strategies, providing a default <see cref="IStrategy"/> implementation scaffold.
    /// </summary>
    public abstract class StrategyBase : IStrategy
    {
        /// <summary>
        /// Called on each bar for the given symbol. Returns zero or more order requests to submit.
        /// </summary>
        public abstract IEnumerable<OrderRequest> OnBar(string symbol, Candle bar, PortfolioSnapshot snapshot);
    }
}
