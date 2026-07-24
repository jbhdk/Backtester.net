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
        public async Task Fetch_EmptyCache_EstablishesCoverageFloorAtFrom()
        {
            string tmp = Path.Combine(Path.GetTempPath(), "bt_fetcher_test", Guid.NewGuid().ToString());
            Directory.CreateDirectory(tmp);
            DateTime from = TruncateToSecond(DateTime.UtcNow).AddDays(-10);
            DateTime to = TruncateToSecond(DateTime.UtcNow);

            IHistoricalDataProvider provider = A.Fake<IHistoricalDataProvider>();
            A.CallTo(() => provider.FetchAsync(A<string>._, A<DateTime>._, A<DateTime>._, A<string>._, A<CancellationToken>._))
                .Returns(Task.FromResult<IEnumerable<Candle>>(new[] { new Candle { Timestamp = from, Open = 1, High = 1, Low = 1, Close = 1, Volume = 1 } }));

            HistoricalDataFetcher fetcher = new(provider, tmp);
            await fetcher.FetchAsync("AAPL", from, to, "1h");

            CoverageFloorLoader floors = new();
            DateTime? floor = floors.Read(Path.Combine(tmp, floors.FileName("AAPL", "1h")));
            Assert.Equal(from, floor);
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
        public async Task Fetch_FromBeforeCoverageFloor_ThrowsWithoutProviderCall()
        {
            string tmp = Path.Combine(Path.GetTempPath(), "bt_fetcher_test", Guid.NewGuid().ToString());
            Directory.CreateDirectory(tmp);
            DateTime now = TruncateToSecond(DateTime.UtcNow);
            DateTime floor = now.AddDays(-100);
            DateTime latest = now.AddDays(-2);

            CsvBarLoader loader = new();
            loader.WriteAll(Path.Combine(tmp, "AAPL_1h.csv"), new[] { new Candle { Timestamp = latest, Open = 1, High = 1, Low = 1, Close = 1, Volume = 1 } });
            CoverageFloorLoader floors = new();
            floors.Write(Path.Combine(tmp, floors.FileName("AAPL", "1h")), floor);

            IHistoricalDataProvider provider = A.Fake<IHistoricalDataProvider>();
            HistoricalDataFetcher fetcher = new(provider, tmp);

            DateTime requestedFrom = now.AddYears(-1);
            DataCoverageException ex = await Assert.ThrowsAsync<DataCoverageException>(
                async () => await fetcher.FetchAsync("AAPL", requestedFrom, now, "1h"));

            Assert.Equal("AAPL", ex.Symbol);
            Assert.Equal(requestedFrom, ex.RequestedFromUtc);
            Assert.Equal(floor, ex.CoverageFloorUtc);
            Assert.Equal("1h", ex.Interval);
            A.CallTo(() => provider.FetchAsync(A<string>._, A<DateTime>._, A<DateTime>._, A<string>._, A<CancellationToken>._))
                .MustNotHaveHappened();
        }

        [Fact]
        public async Task Fetch_FromAtOrAfterFloor_LateListing_ServesCacheWithoutProviderCall()
        {
            string tmp = Path.Combine(Path.GetTempPath(), "bt_fetcher_test", Guid.NewGuid().ToString());
            Directory.CreateDirectory(tmp);
            DateTime now = TruncateToSecond(DateTime.UtcNow);
            DateTime floor = now.AddYears(-2);
            // The symbol's data starts long after the floor (a late listing): the only cached bar is recent,
            // yet the floor confirms we asked from two years ago, so the range is covered and trusted.
            DateTime latest = now.AddDays(-2);

            CsvBarLoader loader = new();
            loader.WriteAll(Path.Combine(tmp, "AAPL_1h.csv"), new[] { new Candle { Timestamp = latest, Open = 1, High = 1, Low = 1, Close = 1, Volume = 1 } });
            CoverageFloorLoader floors = new();
            floors.Write(Path.Combine(tmp, floors.FileName("AAPL", "1h")), floor);

            IHistoricalDataProvider provider = A.Fake<IHistoricalDataProvider>();
            HistoricalDataFetcher fetcher = new(provider, tmp);

            IReadOnlyList<Candle> res = await fetcher.FetchAsync("AAPL", now.AddYears(-1), now, "1h");

            A.CallTo(() => provider.FetchAsync(A<string>._, A<DateTime>._, A<DateTime>._, A<string>._, A<CancellationToken>._))
                .MustNotHaveHappened();
            Assert.Single(res);
        }

        [Fact]
        public async Task Fetch_StaleTailSelfHeal_LeavesCoverageFloorUnchanged()
        {
            string tmp = Path.Combine(Path.GetTempPath(), "bt_fetcher_test", Guid.NewGuid().ToString());
            Directory.CreateDirectory(tmp);
            DateTime baseTime = DateTime.UtcNow.Date.AddDays(-30).AddHours(9);
            DateTime to = baseTime.AddDays(10);

            CsvBarLoader loader = new();
            loader.WriteAll(Path.Combine(tmp, "AAPL_1h.csv"), new[] { new Candle { Timestamp = baseTime, Open = 1, High = 1, Low = 1, Close = 1, Volume = 1 } });
            CoverageFloorLoader floors = new();
            string floorPath = Path.Combine(tmp, floors.FileName("AAPL", "1h"));
            floors.Write(floorPath, baseTime);

            IHistoricalDataProvider provider = A.Fake<IHistoricalDataProvider>();
            A.CallTo(() => provider.FetchAsync(A<string>._, A<DateTime>._, A<DateTime>._, A<string>._, A<CancellationToken>._))
                .Returns(Task.FromResult<IEnumerable<Candle>>(new[] { new Candle { Timestamp = to, Open = 2, High = 2, Low = 2, Close = 2, Volume = 2 } }));

            HistoricalDataFetcher fetcher = new(provider, tmp);
            await fetcher.FetchAsync("AAPL", baseTime, to, "1h");

            A.CallTo(() => provider.FetchAsync(A<string>._, A<DateTime>._, A<DateTime>._, A<string>._, A<CancellationToken>._))
                .MustHaveHappened();
            Assert.Equal(baseTime, floors.Read(floorPath));
        }

        [Fact]
        public async Task ResolveWarmupStart_PrimedCache_ReturnsExactNthBarBeforeTestFrom()
        {
            string tmp = Path.Combine(Path.GetTempPath(), "bt_fetcher_test", Guid.NewGuid().ToString());
            Directory.CreateDirectory(tmp);
            // Daily bars on days 1..10; the Test range starts on day 8, so days 1..7 are candidate warmup
            // bars. Asking for 3 warmup bars must resolve the Data start to day 5 (days 5,6,7 are the lead-in).
            DateTime day1 = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            Candle[] bars = new Candle[10];
            for (int index = 0; index < 10; index++)
            {
                DateTime ts = day1.AddDays(index);
                bars[index] = new Candle { Timestamp = ts, Open = 1, High = 1, Low = 1, Close = 1, Volume = 1 };
            }

            CsvBarLoader loader = new();
            loader.WriteAll(Path.Combine(tmp, "AAPL_1d.csv"), bars);
            CoverageFloorLoader floors = new();
            floors.Write(Path.Combine(tmp, floors.FileName("AAPL", "1d")), day1);

            IHistoricalDataProvider provider = A.Fake<IHistoricalDataProvider>();
            HistoricalDataFetcher fetcher = new(provider, tmp);

            DateTime testFrom = day1.AddDays(7); // day 8
            DateTime resolved = await fetcher.ResolveWarmupStartAsync("AAPL", testFrom, 3, "1d");

            Assert.Equal(day1.AddDays(4), resolved); // day 5
            A.CallTo(() => provider.FetchAsync(A<string>._, A<DateTime>._, A<DateTime>._, A<string>._, A<CancellationToken>._))
                .MustNotHaveHappened();
        }

        [Fact]
        public async Task ResolveWarmupStart_FewerBarsAboveFloorThanRequested_Throws()
        {
            string tmp = Path.Combine(Path.GetTempPath(), "bt_fetcher_test", Guid.NewGuid().ToString());
            Directory.CreateDirectory(tmp);
            // A late listing: the floor reaches back to day 1 but the symbol's data only begins on day 5, so
            // days 5,6,7 are the only bars before the day-8 Test start. Asking for 5 warmup bars must refuse
            // rather than serve the 3 that exist.
            DateTime day1 = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            Candle[] bars = new Candle[6];
            for (int index = 0; index < 6; index++)
            {
                DateTime ts = day1.AddDays(4 + index); // days 5..10
                bars[index] = new Candle { Timestamp = ts, Open = 1, High = 1, Low = 1, Close = 1, Volume = 1 };
            }

            CsvBarLoader loader = new();
            loader.WriteAll(Path.Combine(tmp, "AAPL_1d.csv"), bars);
            CoverageFloorLoader floors = new();
            floors.Write(Path.Combine(tmp, floors.FileName("AAPL", "1d")), day1);

            IHistoricalDataProvider provider = A.Fake<IHistoricalDataProvider>();
            HistoricalDataFetcher fetcher = new(provider, tmp);

            DateTime testFrom = day1.AddDays(7); // day 8
            InsufficientWarmupBarsException ex = await Assert.ThrowsAsync<InsufficientWarmupBarsException>(
                async () => await fetcher.ResolveWarmupStartAsync("AAPL", testFrom, 5, "1d"));

            Assert.Equal("AAPL", ex.Symbol);
            Assert.Equal(5, ex.RequestedBars);
            Assert.Equal(3, ex.AvailableBars);
            Assert.Equal("1d", ex.Interval);
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
