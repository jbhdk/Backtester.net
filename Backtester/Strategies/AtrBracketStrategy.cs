using System;
using System.Collections.Generic;
using System.Linq;
using Backtester.Broker;
using Backtester.Core;

namespace Backtester.Strategies
{
    /// <summary>
    /// Reference strategy that enters with an ATR-based bracket (stop-loss and take-profit)
    /// and trails its stop upward via Modify on each subsequent bar. Re-enters after each round trip.
    /// ATR is pre-computed from the first <see cref="_atrPeriod"/> bars of history in OnStart.
    /// </summary>
    public class AtrBracketStrategy : StrategyBase
    {
        private readonly int _atrPeriod;
        private readonly decimal _stopAtrMultiple;
        private readonly decimal _targetAtrMultiple;

        // Key: symbol → ATR computed from the first _atrPeriod bars of the feed history
        private readonly Dictionary<string, decimal> _atr = new();

        // Key: symbol → bracket handle for the active or pending entry
        private readonly Dictionary<string, BracketHandle> _handles = new();

        // Key: symbol → current trailing stop price
        private readonly Dictionary<string, decimal> _trailingStop = new();

        /// <summary>
        /// Initializes the strategy with the ATR period and stop/target multiples.
        /// </summary>
        public AtrBracketStrategy(int atrPeriod = 14, decimal stopAtrMultiple = 1m, decimal targetAtrMultiple = 2m)
        {
            _atrPeriod = atrPeriod;
            _stopAtrMultiple = stopAtrMultiple;
            _targetAtrMultiple = targetAtrMultiple;
        }

        /// <summary>
        /// Computes ATR for each symbol from the first <see cref="_atrPeriod"/> bars, used as the warm-up window.
        /// </summary>
        public override void OnStart(IReadOnlyDictionary<string, IReadOnlyList<Candle>> history)
        {
            foreach ((string symbol, IReadOnlyList<Candle> bars) in history)
            {
                if (bars.Count < _atrPeriod)
                {
                    continue;
                }

                decimal sum = 0m;
                for (int i = 0; i < _atrPeriod; i++)
                {
                    Candle cur = bars[i];
                    Candle prev = i > 0 ? bars[i - 1] : cur;
                    decimal tr = Math.Max(cur.High - cur.Low,
                                 Math.Max(Math.Abs(cur.High - prev.Close),
                                          Math.Abs(cur.Low - prev.Close)));
                    sum += tr;
                }

                _atr[symbol] = sum / _atrPeriod;
            }
        }

        /// <summary>
        /// Enters with a bracket when flat, trails the stop while in position, and re-enters after each round trip.
        /// </summary>
        public override void OnBar(string symbol, Candle bar, PortfolioSnapshot snapshot, IBroker broker)
        {
            if (!_atr.TryGetValue(symbol, out decimal atr))
            {
                return;
            }

            bool hasPosition = snapshot.Positions.Any(p => p.Symbol == symbol && p.Quantity > 0);

            // Round trip complete: entry had filled (StopOrderId set) but position is now flat
            if (!hasPosition
                && _handles.TryGetValue(symbol, out BracketHandle existing)
                && existing.StopOrderId != null)
            {
                _handles.Remove(symbol);
                _trailingStop.Remove(symbol);
            }

            if (!hasPosition && !_handles.ContainsKey(symbol))
            {
                decimal stop = bar.Close - _stopAtrMultiple * atr;
                decimal target = bar.Close + _targetAtrMultiple * atr;
                BracketHandle handle = broker.SubmitBracket(new BracketRequest
                {
                    Entry = new OrderRequest { Symbol = symbol, Side = OrderSide.Buy, Type = OrderType.Market },
                    StopPrice = stop,
                    TargetPrice = target
                });
                if (handle != null)
                {
                    _handles[symbol] = handle;
                    _trailingStop[symbol] = stop;
                }
            }
            else if (hasPosition
                     && _handles.TryGetValue(symbol, out BracketHandle h)
                     && h.StopOrderId != null)
            {
                decimal newStop = Math.Max(_trailingStop[symbol], bar.Low - _stopAtrMultiple * atr);
                if (newStop > _trailingStop[symbol])
                {
                    _trailingStop[symbol] = newStop;
                    broker.Modify(h.StopOrderId, newStop);
                }
            }
        }
    }
}
