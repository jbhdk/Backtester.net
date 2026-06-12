namespace Backtester.Strategies
{
    using System.Collections.Generic;
    using Backtester.Core;

    public class MovingAverageCrossStrategy : StrategyBase
    {
        public override IEnumerable<OrderRequest> OnBar(string symbol, Candle bar, PortfolioSnapshot snapshot)
        {
            throw new System.NotImplementedException();
        }
    }
}
