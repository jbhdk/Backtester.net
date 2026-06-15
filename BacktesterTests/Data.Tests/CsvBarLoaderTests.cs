using System;
using System.IO;
using System.Linq;
using Backtester.Data;
using Backtester.Core;
using Xunit;
using System.Collections.Generic;

namespace BacktesterTests.Data.Tests
{
    public class CsvBarLoaderTests
    {
        [Fact]
        public void ReadWrite_ReadsBackWrittenCandles()
        {
            string tmp = Path.Combine(Path.GetTempPath(), "bt_test_csvloader", Guid.NewGuid().ToString());
            Directory.CreateDirectory(tmp);
            string path = Path.Combine(tmp, "AAPL_1h.csv");

            CsvBarLoader loader = new();
            DateTime now = TruncateToSecond(DateTime.UtcNow);
            List<Candle> candles = Enumerable.Range(0, 3).Select(i => new Candle
            {
                Timestamp = now.AddHours(i),
                Open = 100 + i,
                High = 101 + i,
                Low = 99 + i,
                Close = 100 + i,
                Volume = 1000 + i
            }).ToList();

            loader.WriteAll(path, candles);

            List<Candle> read = loader.ReadAll(path).ToList();
            Assert.Equal(3, read.Count);
            Assert.Equal(candles[0].Timestamp, read[0].Timestamp);
            Assert.Equal(candles[2].Close, read[2].Close);
        }

        [Fact]
        public void AppendAndMerge_DedupesAndSorts()
        {
            string tmp = Path.Combine(Path.GetTempPath(), "bt_test_csvloader", Guid.NewGuid().ToString());
            Directory.CreateDirectory(tmp);
            string path = Path.Combine(tmp, "AAPL_1h.csv");

            CsvBarLoader loader = new();
            DateTime baseTime = DateTime.UtcNow.Date.AddHours(9);
            Candle[] initial = new[]
            {
                new Candle{ Timestamp = baseTime, Open=1,High=1,Low=1,Close=1,Volume=1},
                new Candle{ Timestamp = baseTime.AddHours(1), Open=2,High=2,Low=2,Close=2,Volume=2}
            };
            loader.WriteAll(path, initial);

            Candle[] additional = new[]
            {
                // duplicate of existing
                new Candle{ Timestamp = baseTime.AddHours(1), Open=2.1m,High=2.1m,Low=2.1m,Close=2.1m,Volume=5},
                // new later
                new Candle{ Timestamp = baseTime.AddHours(2), Open=3,High=3,Low=3,Close=3,Volume=3}
            };

            loader.AppendAndMerge(path, additional);

            List<Candle> merged = loader.ReadAll(path).ToList();
            // should dedupe (timestamp at hour1 kept once) and include hour2
            Assert.Equal(3, merged.Count);
            Assert.Equal(baseTime, merged[0].Timestamp);
            Assert.Equal(baseTime.AddHours(1), merged[1].Timestamp);
            Assert.Equal(baseTime.AddHours(2), merged[2].Timestamp);
            // verify values: duplicated timestamp should be replaced by appended value
            Assert.Equal(1m, merged[0].Open);
            Assert.Equal(2.1m, merged[1].Open);
            Assert.Equal(3m, merged[2].Open);
            Assert.Equal(5m, merged[1].Volume);
        }

        private static DateTime TruncateToSecond(DateTime dt)
        {
            return new DateTime(dt.Ticks - (dt.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc);
        }
    }
}
