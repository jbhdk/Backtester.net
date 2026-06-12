namespace Backtester.Strategies
{
    public abstract class StrategyBase : IStrategy
    {
        public abstract System.Collections.Generic.IEnumerable<Backtester.Core.OrderRequest> OnBar(string symbol, Backtester.Core.Candle bar, Backtester.Core.PortfolioSnapshot snapshot);
    }
}
