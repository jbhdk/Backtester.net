using System;
using System.Collections.Generic;
using System.Linq;
using Backtester.Broker;
using Backtester.Core;

namespace Backtester.Strategies
{
    /// <summary>
    /// Emits a market buy on a golden cross (fast MA crosses above slow MA) and a market sell
    /// on a death cross (fast MA crosses below slow MA), provided a position exists.
    /// All crossover signals are pre-computed in <see cref="OnStart"/> from the full bar history.
    /// </summary>
    public class MovingAverageCrossStrategy : StrategyBase
    {
        private readonly int _fastPeriod;
        private readonly int _slowPeriod;

        // Key: (symbol, bar timestamp) for bars where a golden cross is detected
        private readonly HashSet<(string symbol, DateTime timestamp)> _buySignals = new();

        // Key: (symbol, bar timestamp) for bars where a death cross is detected
        private readonly HashSet<(string symbol, DateTime timestamp)> _sellSignals = new();

        /// <summary>
        /// Initializes the strategy with the fast and slow moving average periods.
        /// </summary>
        public MovingAverageCrossStrategy(int fastPeriod, int slowPeriod)
        {
            _fastPeriod = fastPeriod;
            _slowPeriod = slowPeriod;
        }

        /// <summary>
        /// Scans the full bar history for each symbol and records all crossover timestamps.
        /// </summary>
        public override void OnStart(IReadOnlyDictionary<string, IReadOnlyList<Candle>> history)
        {
            foreach ((string symbol, IReadOnlyList<Candle> bars) in history)
            {
                ComputeSignals(symbol, bars);
            }
        }

        /// <summary>
        /// Acts on a pre-computed crossover at the current bar. A golden cross targets a long, a death
        /// cross targets a short. Because no single fill may flip the position's sign, reversing from one
        /// side to the other emits two market orders — one to flatten the existing position and one to
        /// open the opposite — while entering from flat emits a single order.
        /// </summary>
        public override void OnBar(string symbol, Candle bar, PortfolioSnapshot snapshot, IBroker broker)
        {
            int currentQuantity = snapshot.Positions
                .Where(p => p.Symbol == symbol)
                .Select(p => p.Quantity)
                .FirstOrDefault();

            if (_buySignals.Contains((symbol, bar.Timestamp)) && currentQuantity <= 0)
            {
                if (currentQuantity < 0)
                {
                    broker.Submit(new OrderRequest { Symbol = symbol, Side = OrderSide.Buy, Type = OrderType.Market });
                }

                broker.Submit(new OrderRequest { Symbol = symbol, Side = OrderSide.Buy, Type = OrderType.Market });
            }
            else if (_sellSignals.Contains((symbol, bar.Timestamp)) && currentQuantity >= 0)
            {
                if (currentQuantity > 0)
                {
                    broker.Submit(new OrderRequest { Symbol = symbol, Side = OrderSide.Sell, Type = OrderType.Market });
                }

                broker.Submit(new OrderRequest { Symbol = symbol, Side = OrderSide.Sell, Type = OrderType.Market });
            }
        }

        private void ComputeSignals(string symbol, IReadOnlyList<Candle> bars)
        {
            List<decimal> prices = bars.Select(b => b.Close).ToList();
            bool? lastFastAboveSlow = null;

            for (int i = _slowPeriod - 1; i < prices.Count; i++)
            {
                decimal fastMA = prices.Skip(i - _fastPeriod + 1).Take(_fastPeriod).Average();
                decimal slowMA = prices.Skip(i - _slowPeriod + 1).Take(_slowPeriod).Average();
                bool fastAboveSlow = fastMA > slowMA;

                if (lastFastAboveSlow == null)
                {
                    lastFastAboveSlow = fastAboveSlow;
                    continue;
                }

                if (!lastFastAboveSlow.Value && fastAboveSlow)
                {
                    _buySignals.Add((symbol, bars[i].Timestamp));
                }

                else if (lastFastAboveSlow.Value && !fastAboveSlow)
                {
                    _sellSignals.Add((symbol, bars[i].Timestamp));
                }

                lastFastAboveSlow = fastAboveSlow;
            }
        }
    }
}
