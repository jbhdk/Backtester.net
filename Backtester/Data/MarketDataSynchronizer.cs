using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Backtester.Data
{
    using Backtester.Core;

    // Minimal in-memory synchronizer producing an IMarketDataFeed
    public class MarketDataSynchronizer
    {
        /// <summary>
        /// Fetches candles from each provider concurrently and returns a synchronized feed.
        /// Inject fakes of IHistoricalDataProvider for testing without network calls.
        /// </summary>
        public static async Task<IMarketDataFeed> CreateFromProvidersAsync(
            IReadOnlyDictionary<string, IHistoricalDataProvider> providers,
            DateTime fromUtc,
            DateTime toUtc,
            string interval,
            CancellationToken ct = default)
        {
            string[] symbols = providers.Keys.ToArray();
            Task<IEnumerable<Candle>>[] fetches = symbols
                .Select(symbol => providers[symbol].FetchAsync(symbol, fromUtc, toUtc, interval, ct))
                .ToArray();

            IEnumerable<Candle>[] results = await Task.WhenAll(fetches);

            Dictionary<string, IReadOnlyList<Candle>> series = new Dictionary<string, IReadOnlyList<Candle>>();
            for (int i = 0; i < symbols.Length; i++)
                series[symbols[i]] = results[i].ToList();

            return CreateFromSeries(series);
        }

        /// <summary>
        /// Creates a synchronized feed directly from pre-fetched candle series.
        /// Use this overload in tests to supply candles without a provider.
        /// </summary>
        public static IMarketDataFeed CreateFromSeries(IReadOnlyDictionary<string, IReadOnlyList<Candle>> series)
        {
            return new InMemoryMarketDataFeed(series);
        }

        private class InMemoryMarketDataFeed : IMarketDataFeed
        {
            private readonly List<DateTime> _timeline;
            // Key: symbol/ticker (string) -> sequence of historical candles for that symbol
            private readonly Dictionary<string, IReadOnlyList<Candle>> _series;
            // Key: symbol/ticker (string) -> current index into the series for that symbol
            private readonly Dictionary<string, int> _indices;
            // Key: symbol/ticker (string) -> history buffer (list of candles) for that symbol
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
                var currentTime = _timeline[_pos];

                // for each symbol, advance index while next bar timestamp <= t
                foreach (var sym in _series.Keys)
                {
                    var seriesList = _series[sym];
                    var index = _indices[sym];
                    while (index < seriesList.Count && DateTime.SpecifyKind(seriesList[index].Timestamp, DateTimeKind.Utc) <= currentTime)
                        index++;
                    // index now points to first bar > currentTime; last <= currentTime is index-1
                    _indices[sym] = index;
                    var lastIndex = index - 1;
                    if (lastIndex >= 0)
                    {
                        var latestBar = seriesList[lastIndex];
                        // append copy to history buffer
                        _historyBuffers[sym].Add(latestBar);
                    }
                }

                return true;
            }

            public MarketSlice GetCurrentSlice()
            {
                // Key: symbol/ticker (string) -> latest Candle at current timestamp (may be null)
                var barsBySymbol = new Dictionary<string, Candle>();
                var currentTime = CurrentTime;
                foreach (var sym in _series.Keys)
                {
                    var index = _indices[sym];
                    var lastIndex = index - 1;
                    Candle latestBar = null;
                    if (lastIndex >= 0)
                    {
                        var candidateBar = _series[sym][lastIndex];
                        if (DateTime.SpecifyKind(candidateBar.Timestamp, DateTimeKind.Utc) <= currentTime)
                            latestBar = candidateBar;
                    }
                    barsBySymbol[sym] = latestBar;
                }

                return new MarketSlice { Timestamp = currentTime, BarsBySymbol = barsBySymbol };
            }

            public IReadOnlyList<Candle> GetLookback(string symbol, int lookback)
            {
                if (!_historyBuffers.ContainsKey(symbol)) return Array.Empty<Candle>();
                var buffer = _historyBuffers[symbol];
                if (lookback <= 0) return Array.Empty<Candle>();
                var take = Math.Min(lookback, buffer.Count);
                var result = buffer.Skip(Math.Max(0, buffer.Count - take)).ToList();
                // return newest-first as contract said
                result.Reverse();
                return result;
            }
        }
    }
}
