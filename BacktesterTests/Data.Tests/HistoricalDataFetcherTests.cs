using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Backtester.Core;
using Backtester.Data;
using FakeItEasy;
using Xunit;

namespace BacktesterTests.Data.Tests
{
    public class HistoricalDataFetcherTests
    {
        [Fact]
        public async Task Fetch_NewFile_WritesAndReturns()
        {
            string tmp = Path.Combine(Path.GetTempPath(), "bt_fetcher_test", Guid.NewGuid().ToString());
            Directory.CreateDirectory(tmp);
            DateTime now = DateTime.UtcNow.Date.AddHours(0);
            IHistoricalDataProvider provider = A.Fake<IHistoricalDataProvider>();
            A.CallTo(() => provider.FetchAsync(A<string>._, A<DateTime>._, A<DateTime>._, A<string>._, A<CancellationToken>._))
                .Returns(Task.FromResult<IEnumerable<Candle>>(new[] { new Candle { Timestamp = now, Open = 1, High = 1, Low = 1, Close = 1, Volume = 1 } }));

            HistoricalDataFetcher fetcher = new(provider, tmp);
            IReadOnlyList<Candle> res = await fetcher.FetchAsync("AAPL", now, now, "1h");

            A.CallTo(() => provider.FetchAsync(A<string>._, A<DateTime>._, A<DateTime>._, A<string>._, A<CancellationToken>._))
                .MustHaveHappenedOnceExactly();
            Assert.Single(res);
            string file = Path.Combine(tmp, "AAPL_1h.csv");
            Assert.True(File.Exists(file));
        }

        [Fact]
        public async Task Fetch_FreshCache_UsesCache_NoProviderCall()
        {
            string tmp = Path.Combine(Path.GetTempPath(), "bt_fetcher_test", Guid.NewGuid().ToString());
            Directory.CreateDirectory(tmp);
            DateTime now = TruncateToSecond(DateTime.UtcNow);
            string path = Path.Combine(tmp, "AAPL_1h.csv");

            CsvBarLoader loader = new();
            Candle[] candles = { new() { Timestamp = now, Open = 1, High = 1, Low = 1, Close = 1, Volume = 1 } };
            loader.WriteAll(path, candles);

            IHistoricalDataProvider provider = A.Fake<IHistoricalDataProvider>();
            HistoricalDataFetcher fetcher = new(provider, tmp);

            IReadOnlyList<Candle> res = await fetcher.FetchAsync("AAPL", now, now, "1h");
            A.CallTo(() => provider.FetchAsync(A<string>._, A<DateTime>._, A<DateTime>._, A<string>._, A<CancellationToken>._))
                .MustNotHaveHappened();
            Assert.Single(res);
        }

        private static DateTime TruncateToSecond(DateTime dt)
        {
            return new DateTime(dt.Ticks - (dt.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc);
        }

        [Fact]
        public async Task Fetch_FreshCacheNotReachingTo_NoProviderCall()
        {
            string tmp = Path.Combine(Path.GetTempPath(), "bt_fetcher_test", Guid.NewGuid().ToString());
            Directory.CreateDirectory(tmp);
            DateTime now = TruncateToSecond(DateTime.UtcNow);
            DateTime latest = now.AddHours(-3);
            string path = Path.Combine(tmp, "AAPL_1h.csv");

            CsvBarLoader loader = new();
            Candle[] candles = { new() { Timestamp = latest, Open = 1, High = 1, Low = 1, Close = 1, Volume = 1 } };
            loader.WriteAll(path, candles);

            IHistoricalDataProvider provider = A.Fake<IHistoricalDataProvider>();
            HistoricalDataFetcher fetcher = new(provider, tmp);

            IReadOnlyList<Candle> res = await fetcher.FetchAsync("AAPL", now.AddDays(-5), now, "1h");

            A.CallTo(() => provider.FetchAsync(A<string>._, A<DateTime>._, A<DateTime>._, A<string>._, A<CancellationToken>._))
                .MustNotHaveHappened();
            Assert.Single(res);
        }

        [Fact]
        public async Task Fetch_StaleCache_AppendsMissingRange()
        {
            string tmp = Path.Combine(Path.GetTempPath(), "bt_fetcher_test", Guid.NewGuid().ToString());
            Directory.CreateDirectory(tmp);
            // Historical window whose cached tail lags the requested end by more than the freshness window,
            // so the fetcher extends the tail rather than trusting the stale cache.
            DateTime baseTime = DateTime.UtcNow.Date.AddDays(-30).AddHours(9);
            DateTime to = baseTime.AddDays(10);
            string path = Path.Combine(tmp, "AAPL_1h.csv");

            CsvBarLoader loader = new();
            Candle[] existing = new[] { new Candle { Timestamp = baseTime, Open = 1, High = 1, Low = 1, Close = 1, Volume = 1 } };
            loader.WriteAll(path, existing);

            Candle[] more = new[] { new Candle { Timestamp = to, Open = 2, High = 2, Low = 2, Close = 2, Volume = 2 } };
            IHistoricalDataProvider provider = A.Fake<IHistoricalDataProvider>();
            A.CallTo(() => provider.FetchAsync(A<string>._, A<DateTime>._, A<DateTime>._, A<string>._, A<CancellationToken>._))
                .Returns(Task.FromResult<IEnumerable<Candle>>(more));

            HistoricalDataFetcher fetcher = new(provider, tmp);
            IReadOnlyList<Candle> res = await fetcher.FetchAsync("AAPL", baseTime, to, "1h");

            A.CallTo(() => provider.FetchAsync(A<string>._, A<DateTime>._, A<DateTime>._, A<string>._, A<CancellationToken>._))
                .MustHaveHappened();
            Assert.Equal(2, res.Count);
        }

        [Fact]
        public async Task Fetch_HistoricalToOnClosedDay_NoProviderCall()
        {
            string tmp = Path.Combine(Path.GetTempPath(), "bt_fetcher_test", Guid.NewGuid().ToString());
            Directory.CreateDirectory(tmp);
            // Cached tail is the last trading day (Fri 2010-01-08); the requested end falls on a closed
            // day (Sun 2010-01-10), so no bar can exist past the tail and the cache must be trusted.
            DateTime friday = new(2010, 1, 8, 20, 0, 0, DateTimeKind.Utc);
            DateTime sunday = new(2010, 1, 10, 0, 0, 0, DateTimeKind.Utc);
            string path = Path.Combine(tmp, "AAPL_1d.csv");

            CsvBarLoader loader = new();
            Candle[] candles = { new() { Timestamp = friday, Open = 1, High = 1, Low = 1, Close = 1, Volume = 1 } };
            loader.WriteAll(path, candles);

            IHistoricalDataProvider provider = A.Fake<IHistoricalDataProvider>();
            HistoricalDataFetcher fetcher = new(provider, tmp);

            IReadOnlyList<Candle> res = await fetcher.FetchAsync("AAPL", new DateTime(2010, 1, 1, 0, 0, 0, DateTimeKind.Utc), sunday, "1d");

            A.CallTo(() => provider.FetchAsync(A<string>._, A<DateTime>._, A<DateTime>._, A<string>._, A<CancellationToken>._))
                .MustNotHaveHappened();
            Assert.Single(res);
        }

        [Fact]
        public async Task Fetch_RequestedFromEarlierThanCache_NoProviderCall()
        {
            string tmp = Path.Combine(Path.GetTempPath(), "bt_fetcher_test", Guid.NewGuid().ToString());
            Directory.CreateDirectory(tmp);
            // The symbol's data starts after the requested 'from'; a fresh cache is trusted as-is rather
            // than re-requesting the non-existent earlier bars.
            DateTime now = TruncateToSecond(DateTime.UtcNow);
            DateTime latest = now.AddDays(-2);
            string path = Path.Combine(tmp, "AAPL_1h.csv");

            CsvBarLoader loader = new();
            Candle[] candles = { new() { Timestamp = latest, Open = 1, High = 1, Low = 1, Close = 1, Volume = 1 } };
            loader.WriteAll(path, candles);

            IHistoricalDataProvider provider = A.Fake<IHistoricalDataProvider>();
            HistoricalDataFetcher fetcher = new(provider, tmp);

            IReadOnlyList<Candle> res = await fetcher.FetchAsync("AAPL", now.AddYears(-1), now, "1h");

            A.CallTo(() => provider.FetchAsync(A<string>._, A<DateTime>._, A<DateTime>._, A<string>._, A<CancellationToken>._))
                .MustNotHaveHappened();
            Assert.Single(res);
        }

        [Fact]
        public async Task Fetch_ProviderNotSupported_PropagatesError()
        {
            string tmp = Path.Combine(Path.GetTempPath(), "bt_fetcher_test", Guid.NewGuid().ToString());
            Directory.CreateDirectory(tmp);
            IHistoricalDataProvider provider = A.Fake<IHistoricalDataProvider>();
            A.CallTo(() => provider.FetchAsync(A<string>._, A<DateTime>._, A<DateTime>._, A<string>._, A<CancellationToken>._))
                .ThrowsAsync(new NotSupportedException("interval not supported"));

            HistoricalDataFetcher fetcher = new(provider, tmp);

            await Assert.ThrowsAsync<NotSupportedException>(async () => await fetcher.FetchAsync("AAPL", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow, "1h"));
        }
    }
}
