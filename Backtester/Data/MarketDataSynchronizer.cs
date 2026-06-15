using System;
using System.Collections.Generic;
using System.Linq;

namespace Backtester.Data
{
    using Backtester.Core;

    // Minimal in-memory synchronizer producing an IMarketDataFeed
    public class MarketDataSynchronizer
    {
        public static IMarketDataFeed CreateFromSeries(IReadOnlyDictionary<string, IReadOnlyList<Candle>> series)
        {
            return new InMemoryMarketDataFeed(series);
        }

        private class InMemoryMarketDataFeed : IMarketDataFeed
        {
            private readonly List<DateTime> _timeline;
            private readonly Dictionary<string, IReadOnlyList<Candle>> _series;
            private readonly Dictionary<string, int> _indices;
            private readonly Dictionary<string, List<Candle>> _historyBuffers;
            private int _pos = -1;

            public InMemoryMarketDataFeed(IReadOnlyDictionary<string, IReadOnlyList<Candle>> series)
            {
                _series = series.ToDictionary(kv => kv.Key, kv => kv.Value);
                _indices = _series.Keys.ToDictionary(k => k, v => 0);
                _historyBuffers = _series.Keys.ToDictionary(k => k, v => new List<Candle>());

                // build outer-join timeline
                var times = new HashSet<DateTime>();
                foreach (var list in _series.Values)
                    foreach (var candle in list)
                        times.Add(DateTime.SpecifyKind(candle.Timestamp, DateTimeKind.Utc));

                _timeline = times.OrderBy(t => t).ToList();
            }

            public DateTime CurrentTime => _pos >= 0 && _pos < _timeline.Count ? _timeline[_pos] : DateTime.MinValue;

            public bool Advance()
            {
                if (_pos + 1 >= _timeline.Count) return false;
                _pos++;
                var t = _timeline[_pos];

                // for each symbol, advance index while next bar timestamp <= t
                foreach (var sym in _series.Keys)
                {
                    var list = _series[sym];
                    var idx = _indices[sym];
                    while (idx < list.Count && DateTime.SpecifyKind(list[idx].Timestamp, DateTimeKind.Utc) <= t)
                        idx++;
                    // idx now points to first bar > t; last <= t is idx-1
                    _indices[sym] = idx;
                    var lastIdx = idx - 1;
                    if (lastIdx >= 0)
                    {
                        var bar = list[lastIdx];
                        // append copy to history buffer
                        _historyBuffers[sym].Add(bar);
                    }
                }

                return true;
            }

            public MarketSlice GetCurrentSlice()
            {
                var dict = new Dictionary<string, Candle>();
                var t = CurrentTime;
                foreach (var sym in _series.Keys)
                {
                    var idx = _indices[sym];
                    var lastIdx = idx - 1;
                    Candle bar = null;
                    if (lastIdx >= 0)
                    {
                        var candidate = _series[sym][lastIdx];
                        if (DateTime.SpecifyKind(candidate.Timestamp, DateTimeKind.Utc) <= t)
                            bar = candidate;
                    }
                    dict[sym] = bar;
                }

                return new MarketSlice { Timestamp = t, BarsBySymbol = dict };
            }

            public IReadOnlyList<Candle> GetLookback(string symbol, int lookback)
            {
                if (!_historyBuffers.ContainsKey(symbol)) return Array.Empty<Candle>();
                var buf = _historyBuffers[symbol];
                if (lookback <= 0) return Array.Empty<Candle>();
                var take = Math.Min(lookback, buf.Count);
                var result = buf.Skip(Math.Max(0, buf.Count - take)).ToList();
                // return newest-first as contract said
                result.Reverse();
                return result;
            }
        }
    }
}
