using System.Collections.Generic;

namespace Backtester.Strategies
{
    using Backtester.Core;

    public interface IStrategy
    {
        IEnumerable<OrderRequest> OnBar(string symbol, Candle bar, PortfolioSnapshot snapshot);
    }
}
