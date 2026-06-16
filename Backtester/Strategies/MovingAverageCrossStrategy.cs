using System.Collections.Generic;
using System.Linq;

namespace Backtester.Strategies
{
    using Backtester.Core;

    public class MovingAverageCrossStrategy : StrategyBase
    {
        private readonly int _fastPeriod;
        private readonly int _slowPeriod;
        private readonly Dictionary<string, List<decimal>> _history = new();
        private readonly Dictionary<string, bool?> _lastFastAboveSlow = new();

        public MovingAverageCrossStrategy(int fastPeriod, int slowPeriod)
        {
            _fastPeriod = fastPeriod;
            _slowPeriod = slowPeriod;
        }

        public override IEnumerable<OrderRequest> OnBar(string symbol, Candle bar, PortfolioSnapshot snapshot)
        {
            if (!_history.TryGetValue(symbol, out var prices))
                _history[symbol] = prices = new List<decimal>();

            prices.Add(bar.Close);

            if (prices.Count < _slowPeriod)
                yield break;

            var fastMA = prices.TakeLast(_fastPeriod).Average();
            var slowMA = prices.TakeLast(_slowPeriod).Average();
            var fastAboveSlow = fastMA > slowMA;

            _lastFastAboveSlow.TryGetValue(symbol, out var prev);
            _lastFastAboveSlow[symbol] = fastAboveSlow;

            if (prev == null)
                yield break;

            if (!prev.Value && fastAboveSlow)
            {
                yield return new OrderRequest { Symbol = symbol, Side = OrderSide.Buy, Type = OrderType.Market };
            }
            else if (prev.Value && !fastAboveSlow)
            {
                if (snapshot.Positions.Any(p => p.Symbol == symbol && p.Quantity > 0))
                    yield return new OrderRequest { Symbol = symbol, Side = OrderSide.Sell, Type = OrderType.Market };
            }
        }
    }
}
