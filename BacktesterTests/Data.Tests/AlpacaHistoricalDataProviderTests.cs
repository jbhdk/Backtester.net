using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Alpaca.Markets;
using Backtester.Core;
using Backtester.Data.Alpaca;
using FakeItEasy;
using Xunit;

namespace BacktesterTests.Data.Tests
{
    public class AlpacaHistoricalDataProviderTests
    {
        private static readonly DateTime From = new(2021, 5, 3, 0, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime To   = new(2021, 5, 4, 0, 0, 0, DateTimeKind.Utc);

        [Fact]
        public async Task FetchAsync_SinglePage_MapsBarFieldsToCandle()
        {
            IBar bar = FakeBar(new DateTime(2021, 5, 3, 14, 0, 0, DateTimeKind.Utc), 420.10m, 422.50m, 418.30m, 421.80m, 1_000_000m);
            IAlpacaDataClient client = ClientReturning(Page(null, bar));
            AlpacaHistoricalDataProvider provider = new(client);

            List<Candle> candles = new(await provider.FetchAsync("SPY", From, To, "1d"));

            Candle candle = Assert.Single(candles);
            Assert.Equal(new DateTime(2021, 5, 3, 14, 0, 0, DateTimeKind.Utc), candle.Timestamp);
            Assert.Equal(420.10m, candle.Open);
            Assert.Equal(422.50m, candle.High);
            Assert.Equal(418.30m, candle.Low);
            Assert.Equal(421.80m, candle.Close);
            Assert.Equal(1_000_000m, candle.Volume);
        }

        [Fact]
        public async Task FetchAsync_BarsOutOfOrder_ReturnsCandlesSortedAscending()
        {
            IBar later   = FakeBar(new DateTime(2021, 5, 3, 15, 0, 0, DateTimeKind.Utc), 1m, 1m, 1m, 1m, 1m);
            IBar earlier = FakeBar(new DateTime(2021, 5, 3, 14, 0, 0, DateTimeKind.Utc), 1m, 1m, 1m, 1m, 1m);
            IAlpacaDataClient client = ClientReturning(Page(null, later, earlier));
            AlpacaHistoricalDataProvider provider = new(client);

            List<Candle> candles = new(await provider.FetchAsync("SPY", From, To, "1d"));

            Assert.Equal(2, candles.Count);
            Assert.Equal(new DateTime(2021, 5, 3, 14, 0, 0, DateTimeKind.Utc), candles[0].Timestamp);
            Assert.Equal(new DateTime(2021, 5, 3, 15, 0, 0, DateTimeKind.Utc), candles[1].Timestamp);
        }

        /// <summary>Builds a fake <see cref="IBar"/> exposing the given OHLCV and timestamp.</summary>
        private static IBar FakeBar(DateTime timeUtc, decimal open, decimal high, decimal low, decimal close, decimal volume)
        {
            IBar bar = A.Fake<IBar>();
            A.CallTo(() => bar.TimeUtc).Returns(timeUtc);
            A.CallTo(() => bar.Open).Returns(open);
            A.CallTo(() => bar.High).Returns(high);
            A.CallTo(() => bar.Low).Returns(low);
            A.CallTo(() => bar.Close).Returns(close);
            A.CallTo(() => bar.Volume).Returns(volume);
            return bar;
        }

        /// <summary>Builds a fake <see cref="IPage{IBar}"/> carrying the given bars and next-page token.</summary>
        private static IPage<IBar> Page(string nextPageToken, params IBar[] bars)
        {
            IPage<IBar> page = A.Fake<IPage<IBar>>();
            A.CallTo(() => page.Items).Returns(bars);
            A.CallTo(() => page.NextPageToken).Returns(nextPageToken);
            return page;
        }

        /// <summary>Builds a fake client whose bars call returns the given page.</summary>
        private static IAlpacaDataClient ClientReturning(IPage<IBar> page)
        {
            IAlpacaDataClient client = A.Fake<IAlpacaDataClient>();
            A.CallTo(() => client.ListHistoricalBarsAsync(A<HistoricalBarsRequest>._, A<CancellationToken>._))
                .Returns(Task.FromResult(page));
            return client;
        }
    }
}
