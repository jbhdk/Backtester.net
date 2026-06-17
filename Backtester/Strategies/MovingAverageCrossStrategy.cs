using System.Collections.Generic;
using System.Linq;
using Backtester.Core;

namespace Backtester.Strategies
{
    /// <summary>
    /// Emits a market buy on a golden cross (fast MA crosses above slow MA)
    /// and a market sell on a death cross (fast MA crosses below slow MA), provided a position exists.
    /// </summary>
    public class MovingAverageCrossStrategy : StrategyBase
    {
        private readonly int _fastPeriod;
        private readonly int _slowPeriod;

        // Key: symbol/ticker (string) -> list of recent closing prices for that symbol
        private readonly Dictionary<string, List<decimal>> _history = new();

        // Key: symbol/ticker (string) -> whether fast MA was above slow MA on the previous bar (null = not yet known)
        private readonly Dictionary<string, bool?> _lastFastAboveSlow = new();

        /// <summary>
        /// Initializes the strategy with the fast and slow moving average periods.
        /// </summary>
        public MovingAverageCrossStrategy(int fastPeriod, int slowPeriod)
        {
            _fastPeriod = fastPeriod;
            _slowPeriod = slowPeriod;
        }

        /// <summary>
        /// Calculates moving averages for the symbol and emits orders on crossover events.
        /// </summary>
        public override IEnumerable<OrderRequest> OnBar(string symbol, Candle bar, PortfolioSnapshot snapshot)
        {
            if (!_history.TryGetValue(symbol, out List<decimal> prices))
                _history[symbol] = prices = new List<decimal>();

            prices.Add(bar.Close);

            if (prices.Count < _slowPeriod)
                yield break;

            decimal fastMA = prices.TakeLast(_fastPeriod).Average();
            decimal slowMA = prices.TakeLast(_slowPeriod).Average();
            bool fastAboveSlow = fastMA > slowMA;

            _lastFastAboveSlow.TryGetValue(symbol, out bool? prev);
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
