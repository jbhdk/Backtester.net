using System.Collections.Generic;
using Backtester.Core;

namespace Backtester.Strategies
{
    /// <summary>
    /// Defines the contract for a trading strategy that reacts to market bars.
    /// </summary>
    public interface IStrategy
    {
        /// <summary>
        /// Called on each bar for the given symbol. Returns zero or more order requests to submit.
        /// </summary>
        IEnumerable<OrderRequest> OnBar(string symbol, Candle bar, PortfolioSnapshot snapshot);
    }
}
