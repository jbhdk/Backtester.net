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
    public class DataPrimerTests
    {
        [Fact]
        public async Task Prime_EmptyCache_PersistsBarsAndEstablishesFloorAtFrom()
        {
            string tmp = NewTempFolder();
            DateTime from = TruncateToSecond(DateTime.UtcNow).AddYears(-2);
            DateTime to = TruncateToSecond(DateTime.UtcNow);

            IHistoricalDataProvider provider = A.Fake<IHistoricalDataProvider>();
            A.CallTo(() => provider.FetchAsync(A<string>._, A<DateTime>._, A<DateTime>._, A<string>._, A<CancellationToken>._))
                .Returns(Task.FromResult<IEnumerable<Candle>>(new[] { new Candle { Timestamp = from, Open = 1, High = 1, Low = 1, Close = 1, Volume = 1 } }));

            IDataPrimer primer = new HistoricalDataFetcher(provider, tmp);
            await primer.PrimeAsync(new[] { "AAPL" }, from, to, "1h");

            A.CallTo(() => provider.FetchAsync("AAPL", from, to, "1h", A<CancellationToken>._)).MustHaveHappenedOnceExactly();
            Assert.True(File.Exists(Path.Combine(tmp, "AAPL_1h.csv")));
            CoverageFloorLoader floors = new();
            Assert.Equal(from, floors.Read(Path.Combine(tmp, floors.FileName("AAPL", "1h"))));
        }

        [Fact]
        public async Task Prime_EarlierThanExistingFloor_LowersFloorAndDedupsBars()
        {
            string tmp = NewTempFolder();
            DateTime existingBar = TruncateToSecond(DateTime.UtcNow).AddYears(-1);
            DateTime earlier = existingBar.AddYears(-1);

            CsvBarLoader loader = new();
            loader.WriteAll(Path.Combine(tmp, "AAPL_1h.csv"), new[] { new Candle { Timestamp = existingBar, Open = 1, High = 1, Low = 1, Close = 1, Volume = 1 } });
            CoverageFloorLoader floors = new();
            floors.Write(Path.Combine(tmp, floors.FileName("AAPL", "1h")), existingBar);

            IHistoricalDataProvider provider = A.Fake<IHistoricalDataProvider>();
            // Wholesale fetch returns the new earlier bar plus a re-fetch of the already-cached bar.
            A.CallTo(() => provider.FetchAsync(A<string>._, A<DateTime>._, A<DateTime>._, A<string>._, A<CancellationToken>._))
                .Returns(Task.FromResult<IEnumerable<Candle>>(new[]
                {
                    new Candle { Timestamp = earlier, Open = 2, High = 2, Low = 2, Close = 2, Volume = 2 },
                    new Candle { Timestamp = existingBar, Open = 9, High = 9, Low = 9, Close = 9, Volume = 9 }
                }));

            IDataPrimer primer = new HistoricalDataFetcher(provider, tmp);
            await primer.PrimeAsync(new[] { "AAPL" }, earlier, TruncateToSecond(DateTime.UtcNow), "1h");

            Assert.Equal(earlier, floors.Read(Path.Combine(tmp, floors.FileName("AAPL", "1h"))));
            IReadOnlyList<Candle> cached = loader.ReadAll(Path.Combine(tmp, "AAPL_1h.csv"));
            Assert.Equal(2, cached.Count);
            Assert.Equal(cached.Select(candle => candle.Timestamp).Distinct().Count(), cached.Count);
        }

        [Fact]
        public async Task Prime_LaterThanExistingFloor_LeavesFloorUnchanged()
        {
            string tmp = NewTempFolder();
            DateTime existingFloor = TruncateToSecond(DateTime.UtcNow).AddYears(-2);
            DateTime laterPrimeFrom = existingFloor.AddYears(1);

            CsvBarLoader loader = new();
            loader.WriteAll(Path.Combine(tmp, "AAPL_1h.csv"), new[] { new Candle { Timestamp = existingFloor, Open = 1, High = 1, Low = 1, Close = 1, Volume = 1 } });
            CoverageFloorLoader floors = new();
            string floorPath = Path.Combine(tmp, floors.FileName("AAPL", "1h"));
            floors.Write(floorPath, existingFloor);

            IHistoricalDataProvider provider = A.Fake<IHistoricalDataProvider>();
            A.CallTo(() => provider.FetchAsync(A<string>._, A<DateTime>._, A<DateTime>._, A<string>._, A<CancellationToken>._))
                .Returns(Task.FromResult<IEnumerable<Candle>>(new[] { new Candle { Timestamp = laterPrimeFrom, Open = 2, High = 2, Low = 2, Close = 2, Volume = 2 } }));

            IDataPrimer primer = new HistoricalDataFetcher(provider, tmp);
            await primer.PrimeAsync(new[] { "AAPL" }, laterPrimeFrom, TruncateToSecond(DateTime.UtcNow), "1h");

            Assert.Equal(existingFloor, floors.Read(floorPath));
        }

        [Fact]
        public async Task Prime_LateListedSymbol_LetsSubsequentRunFromFloorBeServed()
        {
            string tmp = NewTempFolder();
            DateTime now = TruncateToSecond(DateTime.UtcNow);
            DateTime primeFrom = now.AddYears(-2);
            // The symbol listed recently: a wholesale fetch over two years yields only recent bars.
            DateTime listingBar = now.AddDays(-2);

            IHistoricalDataProvider provider = A.Fake<IHistoricalDataProvider>();
            A.CallTo(() => provider.FetchAsync(A<string>._, A<DateTime>._, A<DateTime>._, A<string>._, A<CancellationToken>._))
                .Returns(Task.FromResult<IEnumerable<Candle>>(new[] { new Candle { Timestamp = listingBar, Open = 1, High = 1, Low = 1, Close = 1, Volume = 1 } }));

            HistoricalDataFetcher fetcher = new(provider, tmp);
            await fetcher.PrimeAsync(new[] { "AAPL" }, primeFrom, now, "1h");

            IReadOnlyList<Candle> res = await fetcher.FetchAsync("AAPL", primeFrom, now, "1h");

            Assert.Single(res);
            // Only the prime called the Provider; the subsequent run was served from the warm Cache.
            A.CallTo(() => provider.FetchAsync(A<string>._, A<DateTime>._, A<DateTime>._, A<string>._, A<CancellationToken>._))
                .MustHaveHappenedOnceExactly();
        }

        [Fact]
        public async Task Prime_MultipleSymbols_EstablishesCacheAndFloorForEach()
        {
            string tmp = NewTempFolder();
            DateTime from = TruncateToSecond(DateTime.UtcNow).AddYears(-1);
            DateTime to = TruncateToSecond(DateTime.UtcNow);

            IHistoricalDataProvider provider = A.Fake<IHistoricalDataProvider>();
            A.CallTo(() => provider.FetchAsync(A<string>._, A<DateTime>._, A<DateTime>._, A<string>._, A<CancellationToken>._))
                .Returns(Task.FromResult<IEnumerable<Candle>>(new[] { new Candle { Timestamp = from, Open = 1, High = 1, Low = 1, Close = 1, Volume = 1 } }));

            IDataPrimer primer = new HistoricalDataFetcher(provider, tmp);
            await primer.PrimeAsync(new[] { "AAPL", "MSFT" }, from, to, "1h");

            A.CallTo(() => provider.FetchAsync("AAPL", from, to, "1h", A<CancellationToken>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => provider.FetchAsync("MSFT", from, to, "1h", A<CancellationToken>._)).MustHaveHappenedOnceExactly();

            CoverageFloorLoader floors = new();
            Assert.True(File.Exists(Path.Combine(tmp, "AAPL_1h.csv")));
            Assert.True(File.Exists(Path.Combine(tmp, "MSFT_1h.csv")));
            Assert.Equal(from, floors.Read(Path.Combine(tmp, floors.FileName("AAPL", "1h"))));
            Assert.Equal(from, floors.Read(Path.Combine(tmp, floors.FileName("MSFT", "1h"))));
        }

        [Fact]
        public async Task Prime_FreshCacheAlreadyCoveringRange_NoProviderCall()
        {
            string tmp = NewTempFolder();
            DateTime now = TruncateToSecond(DateTime.UtcNow);
            DateTime from = now.AddYears(-2);
            // The cache already spans the requested range: the front is at the floor and the tail is recent,
            // so a re-prime of the same range must not touch the Provider (issue #94).
            CsvBarLoader loader = new();
            loader.WriteAll(Path.Combine(tmp, "AAPL_1h.csv"), new[]
            {
                new Candle { Timestamp = from, Open = 1, High = 1, Low = 1, Close = 1, Volume = 1 },
                new Candle { Timestamp = now.AddDays(-1), Open = 1, High = 1, Low = 1, Close = 1, Volume = 1 }
            });
            CoverageFloorLoader floors = new();
            string floorPath = Path.Combine(tmp, floors.FileName("AAPL", "1h"));
            floors.Write(floorPath, from);

            IHistoricalDataProvider provider = A.Fake<IHistoricalDataProvider>();
            IDataPrimer primer = new HistoricalDataFetcher(provider, tmp);

            await primer.PrimeAsync(new[] { "AAPL" }, from, now, "1h");

            A.CallTo(() => provider.FetchAsync(A<string>._, A<DateTime>._, A<DateTime>._, A<string>._, A<CancellationToken>._))
                .MustNotHaveHappened();
            Assert.Equal(from, floors.Read(floorPath));
        }

        [Fact]
        public async Task Prime_StaleTailOverCoveredFront_FetchesIncrementalTailOnly()
        {
            string tmp = NewTempFolder();
            DateTime now = TruncateToSecond(DateTime.UtcNow);
            DateTime from = now.AddYears(-2);
            // The front is covered by the floor, but the newest cached bar lags the requested end by more than
            // the freshness window: priming must extend only the tail from the latest bar, not re-download the
            // whole range from 'from'.
            DateTime latest = now.AddDays(-30);
            CsvBarLoader loader = new();
            loader.WriteAll(Path.Combine(tmp, "AAPL_1h.csv"), new[] { new Candle { Timestamp = latest, Open = 1, High = 1, Low = 1, Close = 1, Volume = 1 } });
            CoverageFloorLoader floors = new();
            string floorPath = Path.Combine(tmp, floors.FileName("AAPL", "1h"));
            floors.Write(floorPath, from);

            IHistoricalDataProvider provider = A.Fake<IHistoricalDataProvider>();
            A.CallTo(() => provider.FetchAsync(A<string>._, A<DateTime>._, A<DateTime>._, A<string>._, A<CancellationToken>._))
                .Returns(Task.FromResult<IEnumerable<Candle>>(new[] { new Candle { Timestamp = now, Open = 2, High = 2, Low = 2, Close = 2, Volume = 2 } }));

            IDataPrimer primer = new HistoricalDataFetcher(provider, tmp);
            await primer.PrimeAsync(new[] { "AAPL" }, from, now, "1h");

            A.CallTo(() => provider.FetchAsync("AAPL", latest, now, "1h", A<CancellationToken>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => provider.FetchAsync("AAPL", from, now, "1h", A<CancellationToken>._)).MustNotHaveHappened();
            Assert.Equal(from, floors.Read(floorPath));
        }

        private static string NewTempFolder()
        {
            string tmp = Path.Combine(Path.GetTempPath(), "bt_primer_test", Guid.NewGuid().ToString());
            Directory.CreateDirectory(tmp);
            return tmp;
        }

        private static DateTime TruncateToSecond(DateTime dt)
        {
            return new DateTime(dt.Ticks - (dt.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc);
        }
    }
}
