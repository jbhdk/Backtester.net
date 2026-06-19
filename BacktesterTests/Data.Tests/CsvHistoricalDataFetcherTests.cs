using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Backtester.Core;
using Backtester.Data;
using Xunit;

namespace BacktesterTests.Data.Tests
{
    public class CsvHistoricalDataFetcherTests
    {
        [Fact]
        public async Task Fetch_KnownCsv_ReturnsExpectedCandles()
        {
            string tmp = Path.Combine(Path.GetTempPath(), "bt_csv_fetcher_test", Guid.NewGuid().ToString());
            Directory.CreateDirectory(tmp);
            File.WriteAllText(Path.Combine(tmp, "AAPL_1h.csv"),
                "Timestamp,Open,High,Low,Close,Volume\n" +
                "2010-01-04T09:00:00Z,100,101,99,100.5,1000\n" +
                "2010-01-04T10:00:00Z,100.5,102,100,101.5,1200\n");

            CsvHistoricalDataFetcher fetcher = new(tmp);

            IReadOnlyList<Candle> res = await fetcher.FetchAsync(
                "AAPL",
                new DateTime(2010, 1, 4, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2010, 1, 5, 0, 0, 0, DateTimeKind.Utc),
                "1h");

            Assert.Equal(2, res.Count);
            Assert.Equal(new DateTime(2010, 1, 4, 9, 0, 0, DateTimeKind.Utc), res[0].Timestamp);
            Assert.Equal(100.5m, res[0].Close);
            Assert.Equal(101.5m, res[1].Close);
        }

        [Fact]
        public async Task Fetch_CalledTwice_ReturnsIdenticalResults()
        {
            string tmp = Path.Combine(Path.GetTempPath(), "bt_csv_fetcher_test", Guid.NewGuid().ToString());
            Directory.CreateDirectory(tmp);
            File.WriteAllText(Path.Combine(tmp, "AAPL_1h.csv"),
                "Timestamp,Open,High,Low,Close,Volume\n" +
                "2010-01-04T09:00:00Z,100,101,99,100.5,1000\n" +
                "2010-01-04T10:00:00Z,100.5,102,100,101.5,1200\n");

            CsvHistoricalDataFetcher fetcher = new(tmp);
            DateTime from = new(2010, 1, 4, 0, 0, 0, DateTimeKind.Utc);
            DateTime to = new(2010, 1, 5, 0, 0, 0, DateTimeKind.Utc);

            IReadOnlyList<Candle> first = await fetcher.FetchAsync("AAPL", from, to, "1h");
            IReadOnlyList<Candle> second = await fetcher.FetchAsync("AAPL", from, to, "1h");

            Assert.Equal(first.Count, second.Count);
            for (int i = 0; i < first.Count; i++)
            {
                Assert.Equal(first[i].Timestamp, second[i].Timestamp);
                Assert.Equal(first[i].Close, second[i].Close);
                Assert.Equal(first[i].Volume, second[i].Volume);
            }
        }

        [Fact]
        public async Task Fetch_NoFileForSymbol_ReturnsEmpty()
        {
            string tmp = Path.Combine(Path.GetTempPath(), "bt_csv_fetcher_test", Guid.NewGuid().ToString());
            Directory.CreateDirectory(tmp);

            CsvHistoricalDataFetcher fetcher = new(tmp);

            IReadOnlyList<Candle> res = await fetcher.FetchAsync(
                "MSFT",
                new DateTime(2010, 1, 4, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2010, 1, 5, 0, 0, 0, DateTimeKind.Utc),
                "1h");

            Assert.Empty(res);
        }
    }
}
