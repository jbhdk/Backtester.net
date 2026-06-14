using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
            var tmp = Path.Combine(Path.GetTempPath(), "bt_fetcher_test", Guid.NewGuid().ToString());
            Directory.CreateDirectory(tmp);
            var now = DateTime.UtcNow.Date.AddHours(0);
            var provider = A.Fake<IHistoricalDataProvider>();
            A.CallTo(() => provider.FetchAsync(A<string>._, A<DateTime>._, A<DateTime>._, A<string>._, A<CancellationToken>._))
                .Returns(Task.FromResult<IEnumerable<Candle>>(new[] { new Candle { Timestamp = now, Open = 1, High = 1, Low = 1, Close = 1, Volume = 1 } }));

            var fetcher = new HistoricalDataFetcher(provider, tmp);
            var res = await fetcher.FetchAsync("AAPL", now, now, "1h");

            A.CallTo(() => provider.FetchAsync(A<string>._, A<DateTime>._, A<DateTime>._, A<string>._, A<CancellationToken>._))
                .MustHaveHappenedOnceExactly();
            Assert.Single(res);
            var file = Path.Combine(tmp, "AAPL_1h.csv");
            Assert.True(File.Exists(file));
        }

        [Fact]
        public async Task Fetch_FreshCache_UsesCache_NoProviderCall()
        {
            var tmp = Path.Combine(Path.GetTempPath(), "bt_fetcher_test", Guid.NewGuid().ToString());
            Directory.CreateDirectory(tmp);
            var now = TruncateToSecond(DateTime.UtcNow);
            var path = Path.Combine(tmp, "AAPL_1h.csv");

            var loader = new CsvBarLoader();
            var candles = new[] { new Candle { Timestamp = now, Open = 1, High = 1, Low = 1, Close = 1, Volume = 1 } };
            loader.WriteAll(path, candles);

            var provider = A.Fake<IHistoricalDataProvider>();
            var fetcher = new HistoricalDataFetcher(provider, tmp);

            var res = await fetcher.FetchAsync("AAPL", now, now, "1h");
            A.CallTo(() => provider.FetchAsync(A<string>._, A<DateTime>._, A<DateTime>._, A<string>._, A<CancellationToken>._))
                .MustNotHaveHappened();
            Assert.Single(res);
        }

        private static DateTime TruncateToSecond(DateTime dt)
        {
            return new DateTime(dt.Ticks - (dt.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc);
        }

        [Fact]
        public async Task Fetch_StaleCache_AppendsMissingRange()
        {
            var tmp = Path.Combine(Path.GetTempPath(), "bt_fetcher_test", Guid.NewGuid().ToString());
            Directory.CreateDirectory(tmp);
            var baseTime = DateTime.UtcNow.Date.AddDays(-10).AddHours(9);
            var path = Path.Combine(tmp, "AAPL_1h.csv");

            var loader = new CsvBarLoader();
            var existing = new[] { new Candle { Timestamp = baseTime, Open = 1, High = 1, Low = 1, Close = 1, Volume = 1 } };
            loader.WriteAll(path, existing);

            var more = new[] { new Candle { Timestamp = baseTime.AddHours(1), Open = 2, High = 2, Low = 2, Close = 2, Volume = 2 } };
            var provider = A.Fake<IHistoricalDataProvider>();
            A.CallTo(() => provider.FetchAsync(A<string>._, A<DateTime>._, A<DateTime>._, A<string>._, A<CancellationToken>._))
                .Returns(Task.FromResult<IEnumerable<Candle>>(more));

            var fetcher = new HistoricalDataFetcher(provider, tmp);
            var res = await fetcher.FetchAsync("AAPL", baseTime, baseTime.AddHours(1), "1h");

            A.CallTo(() => provider.FetchAsync(A<string>._, A<DateTime>._, A<DateTime>._, A<string>._, A<CancellationToken>._))
                .MustHaveHappened();
            Assert.Equal(2, res.Count);
        }

        [Fact]
        public async Task Fetch_ProviderNotSupported_PropagatesError()
        {
            var tmp = Path.Combine(Path.GetTempPath(), "bt_fetcher_test", Guid.NewGuid().ToString());
            Directory.CreateDirectory(tmp);
            var provider = A.Fake<IHistoricalDataProvider>();
            A.CallTo(() => provider.FetchAsync(A<string>._, A<DateTime>._, A<DateTime>._, A<string>._, A<CancellationToken>._))
                .ThrowsAsync(new NotSupportedException("interval not supported"));

            var fetcher = new HistoricalDataFetcher(provider, tmp);

            await Assert.ThrowsAsync<NotSupportedException>(async () => await fetcher.FetchAsync("AAPL", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow, "1h"));
        }
    }
}
