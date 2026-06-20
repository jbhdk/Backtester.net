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
        public async Task FetchAsync_MultiplePages_ReturnsBarsFromEveryPage()
        {
            List<Call> calls = new();
            IBar onPage1 = FakeBar(new DateTime(2021, 5, 3, 14, 0, 0, DateTimeKind.Utc), 1m, 1m, 1m, 1m, 1m);
            IBar onPage2 = FakeBar(new DateTime(2021, 5, 3, 15, 0, 0, DateTimeKind.Utc), 2m, 2m, 2m, 2m, 2m);
            IAlpacaDataClient client = SequencedClient(calls, Page("TOKEN1", onPage1), Page(null, onPage2));
            AlpacaHistoricalDataProvider provider = new(client);

            List<Candle> candles = new(await provider.FetchAsync("SPY", From, To, "1d"));

            Assert.Equal(2, candles.Count);
            Assert.Equal(new DateTime(2021, 5, 3, 14, 0, 0, DateTimeKind.Utc), candles[0].Timestamp);
            Assert.Equal(new DateTime(2021, 5, 3, 15, 0, 0, DateTimeKind.Utc), candles[1].Timestamp);
            Assert.Equal(2, calls.Count);
        }

        [Fact]
        public async Task FetchAsync_RequestsMaximumPageSize()
        {
            List<Call> calls = new();
            IAlpacaDataClient client = SequencedClient(calls, Page(null));
            AlpacaHistoricalDataProvider provider = new(client);

            await provider.FetchAsync("SPY", From, To, "1d");

            Assert.Equal(Pagination.MaxPageSize, Assert.Single(calls).PageSize.Value);
        }

        [Fact]
        public async Task FetchAsync_ThreadsNextPageTokenIntoFollowUpCall()
        {
            List<Call> calls = new();
            IAlpacaDataClient client = SequencedClient(calls, Page("TOKEN1"), Page(null));
            AlpacaHistoricalDataProvider provider = new(client);

            await provider.FetchAsync("SPY", From, To, "1d");

            Assert.Equal(2, calls.Count);
            Assert.True(string.IsNullOrEmpty(calls[0].PageToken));
            Assert.Equal("TOKEN1", calls[1].PageToken);
        }

        [Fact]
        public async Task FetchAsync_ForwardsCancellationTokenToEveryPageCall()
        {
            List<Call> calls = new();
            IAlpacaDataClient client = SequencedClient(calls, Page("TOKEN1"), Page(null));
            AlpacaHistoricalDataProvider provider = new(client);
            using CancellationTokenSource cts = new();

            await provider.FetchAsync("SPY", From, To, "1d", cts.Token);

            Assert.Equal(2, calls.Count);
            Assert.All(calls, call => Assert.Equal(cts.Token, call.Ct));
        }

        [Fact]
        public async Task FetchAsync_ClientRaisesRestClientError_PropagatesItUnwrapped()
        {
            IAlpacaDataClient client = A.Fake<IAlpacaDataClient>();
            RestClientErrorException raised = new("Alpaca SIP entitlement required.");
            A.CallTo(() => client.ListHistoricalBarsAsync(A<HistoricalBarsRequest>._, A<CancellationToken>._))
                .Throws(raised);
            AlpacaHistoricalDataProvider provider = new(client);

            RestClientErrorException thrown = await Assert.ThrowsAsync<RestClientErrorException>(
                () => provider.FetchAsync("SPY", From, To, "1d"));

            Assert.Same(raised, thrown);
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

        /// <summary>A snapshot of one bars call: the request's page token and size, and the token at call time.</summary>
        private readonly struct Call
        {
            public Call(string pageToken, uint? pageSize, CancellationToken ct)
            {
                PageToken = pageToken;
                PageSize = pageSize;
                Ct = ct;
            }

            public string PageToken { get; }
            public uint? PageSize { get; }
            public CancellationToken Ct { get; }
        }

        /// <summary>
        /// Builds a fake client that returns the given pages on successive calls, recording a
        /// snapshot of each call's pagination state (token and size) and cancellation token.
        /// </summary>
        private static IAlpacaDataClient SequencedClient(IList<Call> calls, params IPage<IBar>[] pages)
        {
            IAlpacaDataClient client = A.Fake<IAlpacaDataClient>();
            int index = 0;
            A.CallTo(() => client.ListHistoricalBarsAsync(A<HistoricalBarsRequest>._, A<CancellationToken>._))
                .ReturnsLazily((HistoricalBarsRequest request, CancellationToken ct) =>
                {
                    calls.Add(new Call(request.Pagination.Token, request.Pagination.Size, ct));
                    IPage<IBar> page = pages[Math.Min(index, pages.Length - 1)];
                    index++;
                    return Task.FromResult(page);
                });
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
