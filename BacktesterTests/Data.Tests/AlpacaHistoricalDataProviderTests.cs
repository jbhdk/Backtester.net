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

        [Theory]
        [InlineData("5m",  5, BarTimeFrameUnit.Minute)]
        [InlineData("15m", 15, BarTimeFrameUnit.Minute)]
        [InlineData("2h",  2, BarTimeFrameUnit.Hour)]
        [InlineData("1h",  1, BarTimeFrameUnit.Hour)]
        [InlineData("1d",  1, BarTimeFrameUnit.Day)]
        [InlineData("1wk", 1, BarTimeFrameUnit.Week)]
        [InlineData("1mo", 1, BarTimeFrameUnit.Month)]
        public async Task FetchAsync_Interval_IsParsedIntoBarTimeFrame(string interval, int expectedValue, BarTimeFrameUnit expectedUnit)
        {
            List<HistoricalBarsRequest> captured = new();
            IAlpacaDataClient client = CapturingClient(Page(null), captured);
            AlpacaHistoricalDataProvider provider = new(client);

            await provider.FetchAsync("SPY", From, To, interval);

            HistoricalBarsRequest request = Assert.Single(captured);
            Assert.Equal(expectedValue, request.TimeFrame.Value);
            Assert.Equal(expectedUnit, request.TimeFrame.Unit);
        }

        [Fact]
        public async Task FetchAsync_OverriddenFeedAndAdjustment_ReachTheRequest()
        {
            List<HistoricalBarsRequest> captured = new();
            IAlpacaDataClient client = CapturingClient(Page(null), captured);
            AlpacaHistoricalDataProvider provider = new(client, MarketDataFeed.Iex, Adjustment.SplitsAndDividends);

            await provider.FetchAsync("SPY", From, To, "1d");

            HistoricalBarsRequest request = Assert.Single(captured);
            Assert.Equal(MarketDataFeed.Iex, request.Feed.Value);
            Assert.Equal(Adjustment.SplitsAndDividends, request.Adjustment.Value);
        }

        [Fact]
        public async Task FetchAsync_ByDefault_RequestsSipFeedAndSplitAdjustment()
        {
            List<HistoricalBarsRequest> captured = new();
            IAlpacaDataClient client = CapturingClient(Page(null), captured);
            AlpacaHistoricalDataProvider provider = new(client);

            await provider.FetchAsync("SPY", From, To, "1d");

            HistoricalBarsRequest request = Assert.Single(captured);
            Assert.Equal(MarketDataFeed.Sip, request.Feed.Value);
            Assert.Equal(Adjustment.SplitsOnly, request.Adjustment.Value);
        }

        [Fact]
        public async Task FetchAsync_UnsupportedInterval_ThrowsBeforeCallingClient()
        {
            IAlpacaDataClient client = ClientReturning(Page(null));
            AlpacaHistoricalDataProvider provider = new(client);

            await Assert.ThrowsAsync<NotSupportedException>(() => provider.FetchAsync("SPY", From, To, "3y"));

            A.CallTo(() => client.ListHistoricalBarsAsync(A<HistoricalBarsRequest>._, A<CancellationToken>._))
                .MustNotHaveHappened();
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

        /// <summary>Builds a fake client that returns the given page and records each request it receives.</summary>
        private static IAlpacaDataClient CapturingClient(IPage<IBar> page, IList<HistoricalBarsRequest> captured)
        {
            IAlpacaDataClient client = A.Fake<IAlpacaDataClient>();
            A.CallTo(() => client.ListHistoricalBarsAsync(A<HistoricalBarsRequest>._, A<CancellationToken>._))
                .Invokes((HistoricalBarsRequest request, CancellationToken ct) => captured.Add(request))
                .Returns(Task.FromResult(page));
            return client;
        }
    }
}
