using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Backtester.Core;
using Backtester.Data;
using Backtester.Optimization;
using Xunit;

namespace BacktesterTests.Optimization.Tests
{
    /// <summary>
    /// Behaviour of the in-memory fetcher's warmup-resolution seam (ADR 0022): it resolves "N bars before
    /// the Test start" against its pre-fetched series, or refuses when fewer than N bars precede the Test
    /// start, so a bar-count warmup resolves consistently whether run on the cache-aware fetcher or the
    /// Optimizer's shared in-memory bars.
    /// </summary>
    public class InMemoryHistoricalDataFetcherTests
    {
        private static readonly DateTime Day1 = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static Candle Bar(DateTime ts)
        {
            return new() { Timestamp = ts, Open = 1, High = 1, Low = 1, Close = 1, Volume = 1 };
        }

        private static IReadOnlyDictionary<string, IReadOnlyList<Candle>> SeriesFor(string symbol, int bars)
        {
            List<Candle> candles = new(bars);
            for (int index = 0; index < bars; index++)
            {
                candles.Add(Bar(Day1.AddDays(index)));
            }

            // Key: symbol/ticker -> its pre-fetched candle series.
            return new Dictionary<string, IReadOnlyList<Candle>> { [symbol] = candles };
        }

        [Fact]
        public async Task ResolveWarmupStart_ReturnsExactNthBarBeforeTestFrom()
        {
            // Days 1..10 in memory; Test starts on day 8, so days 1..7 are candidate warmup bars. Asking for
            // 3 resolves the Data start to day 5.
            InMemoryHistoricalDataFetcher fetcher = new(SeriesFor("AAPL", 10));

            DateTime resolved = await fetcher.ResolveWarmupStartAsync("AAPL", Day1.AddDays(7), 3, "1d");

            Assert.Equal(Day1.AddDays(4), resolved); // day 5
        }

        [Fact]
        public async Task ResolveWarmupStart_FewerBarsBeforeTestFromThanRequested_Throws()
        {
            // Only days 1..5 in memory; Test starts on day 4, so only days 1..3 precede it. Asking for 5
            // warmup bars must refuse rather than serve the 3 that exist.
            InMemoryHistoricalDataFetcher fetcher = new(SeriesFor("AAPL", 5));

            InsufficientWarmupBarsException ex = await Assert.ThrowsAsync<InsufficientWarmupBarsException>(
                async () => await fetcher.ResolveWarmupStartAsync("AAPL", Day1.AddDays(3), 5, "1d"));

            Assert.Equal("AAPL", ex.Symbol);
            Assert.Equal(5, ex.RequestedBars);
            Assert.Equal(3, ex.AvailableBars);
        }
    }
}
