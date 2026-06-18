using System;
using System.Collections.Generic;
using System.Linq;
using Backtester.Core;

namespace Backtester.Engine
{
    /// <summary>
    /// Synchronizes one or more per-symbol candle series onto a common outer-join timeline and yields
    /// the ordered sequence of <see cref="MarketSlice"/> snapshots the engine processes, one per timestamp.
    /// </summary>
    internal sealed class SliceSequence
    {
        // Key: symbol/ticker (string) -> ascending candle series for that symbol
        private readonly IReadOnlyDictionary<string, IReadOnlyList<Candle>> _series;

        /// <summary>
        /// Initializes the sequence from per-symbol candle series. Each series is assumed sorted ascending by timestamp.
        /// </summary>
        public SliceSequence(IReadOnlyDictionary<string, IReadOnlyList<Candle>> series)
        {
            _series = series ?? throw new ArgumentNullException(nameof(series));
        }

        /// <summary>
        /// Yields one slice per distinct timestamp across all symbols, in ascending order. Each slice carries,
        /// per symbol, the most recent bar at or before that timestamp (null until the symbol's first bar),
        /// forward-filling symbols that have no bar exactly at the timestamp.
        /// </summary>
        public IEnumerable<MarketSlice> Slices()
        {
            List<DateTime> timeline = BuildTimeline();

            // Key: symbol/ticker (string) -> index of the first unconsumed candle in that symbol's series
            Dictionary<string, int> indices = _series.Keys.ToDictionary(symbol => symbol, symbol => 0);

            foreach (DateTime timestamp in timeline)
            {
                // Key: symbol/ticker (string) -> latest bar at or before the timestamp (null if none yet)
                Dictionary<string, Candle> barsBySymbol = new();
                foreach (string symbol in _series.Keys)
                {
                    IReadOnlyList<Candle> seriesList = _series[symbol];
                    int index = indices[symbol];
                    while (index < seriesList.Count && DateTime.SpecifyKind(seriesList[index].Timestamp, DateTimeKind.Utc) <= timestamp)
                    {
                        index++;
                    }

                    indices[symbol] = index;
                    int lastIndex = index - 1;
                    barsBySymbol[symbol] = lastIndex >= 0 ? seriesList[lastIndex] : null;
                }

                yield return new MarketSlice { Timestamp = timestamp, BarsBySymbol = barsBySymbol };
            }
        }

        private List<DateTime> BuildTimeline()
        {
            HashSet<DateTime> times = new();
            foreach (IReadOnlyList<Candle> seriesList in _series.Values)
            {
                foreach (Candle candle in seriesList)
                {
                    times.Add(DateTime.SpecifyKind(candle.Timestamp, DateTimeKind.Utc));
                }
            }

            return times.OrderBy(timestamp => timestamp).ToList();
        }
    }
}
